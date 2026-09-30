using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using FluentAssertions;
using FluentAssertions.Execution;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands.Typed.CallbackQueries;

public sealed class CallbackQueryCommandTests
{
    private static readonly ITelegramBotClient Client = Mock.Of<ITelegramBotClient>();

    [Fact]
    public async Task CanHandleAsync_WhenTheDataParses_ShouldHandleWithTheParsedValue()
    {
        // Arrange
        var sut = new PageCommand();
        var request = Tap("page:3");

        // Act
        var canHandle = await sut.CanHandleAsync(request, CancellationToken.None);
        await sut.HandleAsync(request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeTrue();
            sut.HandledPage.Should().Be(3);
            sut.ParseCalls.Should().Be(1);
        }
    }

    [Theory]
    [InlineData("page:three")]
    [InlineData("open:3")]
    public async Task CanHandleAsync_WhenTheDataDoesNotParse_ShouldDecline(string data)
    {
        // Arrange
        var sut = new PageCommand();

        // Act
        var canHandle = await sut.CanHandleAsync(Tap(data), CancellationToken.None);

        // Assert
        canHandle.Should().BeFalse();
    }

    [Fact]
    public async Task CanHandleAsync_WhenTheQueryCarriesNoData_ShouldDeclineWithoutParsing()
    {
        // Arrange
        var sut = new PageCommand();

        // Act
        var canHandle = await sut.CanHandleAsync(Tap(data: null), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeFalse();
            sut.ParseCalls.Should().Be(0);
        }
    }

    [Fact]
    public async Task CanHandleAsync_ForAButtonType_ShouldDecodeWithoutAParseOverride()
    {
        // Arrange
        var sut = new CupCommand();
        var request = Tap("cbq-cup:2");

        // Act
        var canHandle = await sut.CanHandleAsync(request, CancellationToken.None);
        await sut.HandleAsync(request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeTrue();
            sut.Handled.Should().Be(new Cup(2));
        }
    }

    [Theory]
    [InlineData("cbq-kettle:2")]
    [InlineData("kettle=2")]
    public async Task CanHandleAsync_WithAParseOverride_ShouldStillAcceptALegacyFormat(string data)
    {
        // Arrange
        var sut = new KettleCommand();
        var request = Tap(data);

        // Act
        var canHandle = await sut.CanHandleAsync(request, CancellationToken.None);
        await sut.HandleAsync(request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeTrue();
            sut.Handled.Should().Be(new Kettle(2));
        }
    }

    [Fact]
    public async Task CanHandleAsync_WhenCheckDeclines_ShouldDecline()
    {
        // Arrange
        var sut = new CupCommand { Check = ButtonCheck.Decline };

        // Act
        var canHandle = await sut.CanHandleAsync(Tap("cbq-cup:2"), CancellationToken.None);

        // Assert
        canHandle.Should().BeFalse();
    }

    [Fact]
    public async Task CanHandleAsync_ShouldCheckOnlyAfterParsing()
    {
        // Arrange
        var sut = new CupCommand();

        // Act
        await sut.CanHandleAsync(Tap("cbq-kettle:1"), CancellationToken.None);
        await sut.CanHandleAsync(Tap("cbq-cup:3"), CancellationToken.None);

        // Assert: the other command's data is never checked, and a check sees the parsed value.
        sut.Checked.Should().Equal(new Cup(3));
    }

    [Fact]
    public async Task HandleAsync_WhenCheckRejects_ShouldAnswerInsteadOfHandling()
    {
        // Arrange
        var client = new Mock<ITelegramBotClient>();
        var sut = new CupCommand { Check = ButtonCheck.Reject("Not your cup", showAlert: true) };
        var request = Tap("cbq-cup:2", client.Object);

        // Act
        var canHandle = await sut.CanHandleAsync(request, CancellationToken.None);
        await sut.HandleAsync(request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            canHandle.Should().BeTrue();
            sut.Handled.Should().BeNull();
            client.Verify(
                x =>
                    x.SendRequest(
                        It.Is<AnswerCallbackQueryRequest>(answer =>
                            answer.CallbackQueryId == "query" && answer.Text == "Not your cup" && answer.ShowAlert
                        ),
                        It.IsAny<CancellationToken>()
                    ),
                Times.Once
            );
        }
    }

    [Fact]
    public async Task HandleAsync_WhenRejectedWithoutAnAnswer_ShouldAnswerSilently()
    {
        // Arrange
        var client = new Mock<ITelegramBotClient>();
        var sut = new CupCommand { Check = ButtonCheck.Reject() };
        var request = Tap("cbq-cup:2", client.Object);

        // Act
        await sut.CanHandleAsync(request, CancellationToken.None);
        await sut.HandleAsync(request, CancellationToken.None);

        // Assert
        client.Verify(
            x =>
                x.SendRequest(
                    It.Is<AnswerCallbackQueryRequest>(answer => answer.Text == null && !answer.ShowAlert),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task HandleAsync_WhenTheRejectionIsAnsweredTooLate_ShouldSwallowIt()
    {
        // Arrange
        var client = new Mock<ITelegramBotClient>();
        client
            .Setup(x => x.SendRequest(It.IsAny<AnswerCallbackQueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new ApiRequestException(
                    "Bad Request: query is too old and response timeout expired or query ID is invalid",
                    400
                )
            );
        var sut = new CupCommand { Check = ButtonCheck.Reject("Not your cup") };
        var request = Tap("cbq-cup:2", client.Object);
        await sut.CanHandleAsync(request, CancellationToken.None);

        // Act
        var act = () => sut.HandleAsync(request, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    private static CommandRequest Tap(string? data, ITelegramBotClient? client = null) =>
        new(
            new Update
            {
                CallbackQuery = new CallbackQuery
                {
                    Id = "query",
                    Data = data,
                    From = new User { Id = 7 },
                    Message = new Message { Chat = new Chat { Id = 42 } },
                },
            },
            client ?? Client
        );

    private sealed class PageCommand : CallbackQueryCommand<int>
    {
        public int ParseCalls { get; private set; }

        public int? HandledPage { get; private set; }

        protected override int? Parse(string data)
        {
            ParseCalls++;
            return data.StartsWith("page:") && int.TryParse(data["page:".Length..], out var page) ? page : null;
        }

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            HandledPage = Parsed;
            return Task.CompletedTask;
        }
    }

    [ButtonData("cbq-cup")]
    private readonly record struct Cup(int Size);

    // Decodes Cup without any code of its own; the check is the test's to set.
    private sealed class CupCommand : CallbackQueryCommand<Cup>
    {
        public ButtonCheck Check { get; init; } = ButtonCheck.Accept;

        public List<Cup> Checked { get; } = [];

        public Cup? Handled { get; private set; }

        protected override Task<ButtonCheck> CheckAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        )
        {
            Checked.Add(Parsed);
            return Task.FromResult(Check);
        }

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            Handled = Parsed;
            return Task.CompletedTask;
        }
    }

    [ButtonData("cbq-kettle")]
    private readonly record struct Kettle(int Cups);

    // Also reads "kettle=2", the form its buttons had before Kettle was typed.
    private sealed class KettleCommand : CallbackQueryCommand<Kettle>
    {
        public Kettle? Handled { get; private set; }

        protected override Kettle? Parse(string data) =>
            base.Parse(data)
            ?? (
                data.StartsWith("kettle=") && int.TryParse(data["kettle=".Length..], out var cups)
                    ? new Kettle(cups)
                    : null
            );

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            Handled = Parsed;
            return Task.CompletedTask;
        }
    }
}
