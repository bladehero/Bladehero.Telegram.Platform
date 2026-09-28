using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using FluentAssertions;
using FluentAssertions.Execution;
using Moq;
using Telegram.Bot;
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

    private static CommandRequest Tap(string? data) =>
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
            Client
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
}
