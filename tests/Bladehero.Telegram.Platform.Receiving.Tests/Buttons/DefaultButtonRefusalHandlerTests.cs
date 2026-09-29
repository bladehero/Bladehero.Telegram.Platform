using Bladehero.Telegram.Platform.Receiving.Buttons;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Buttons;

public sealed class DefaultButtonRefusalHandlerTests
{
    private readonly Mock<ITelegramBotClient> _client = new();
    private readonly DefaultButtonRefusalHandler _sut = new(NullLogger<DefaultButtonRefusalHandler>.Instance);

    [Fact]
    public async Task HandleAsync_WhenNoLongerActive_ShouldSaySoInANotification()
    {
        // Act
        await _sut.HandleAsync(Refusal(ButtonRefusalReason.NoLongerActive), CancellationToken.None);

        // Assert
        _client.Verify(
            x =>
                x.SendRequest(
                    It.Is<AnswerCallbackQueryRequest>(request =>
                        request.CallbackQueryId == "query"
                        && request.Text == "That button is no longer active."
                        && !request.ShowAlert
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task HandleAsync_WhenUnclaimed_ShouldAnswerSilently()
    {
        // Act
        await _sut.HandleAsync(Refusal(ButtonRefusalReason.Unclaimed), CancellationToken.None);

        // Assert
        _client.Verify(
            x =>
                x.SendRequest(
                    It.Is<AnswerCallbackQueryRequest>(request =>
                        request.CallbackQueryId == "query" && request.Text == null && !request.ShowAlert
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task HandleAsync_WhenTheQueryIsTooOld_ShouldSwallowIt()
    {
        // Arrange
        Refuse(400, "Bad Request: query is too old and response timeout expired or query ID is invalid");

        // Act
        var act = () => _sut.HandleAsync(Refusal(ButtonRefusalReason.Unclaimed), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenTelegramRefusesOtherwise_ShouldThrow()
    {
        // Arrange
        Refuse(429, "Too Many Requests: retry after 5");

        // Act
        var act = () => _sut.HandleAsync(Refusal(ButtonRefusalReason.Unclaimed), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ApiRequestException>().WithMessage("Too Many Requests: retry after 5");
    }

    private ButtonRefusal Refusal(ButtonRefusalReason reason) =>
        new(
            new CallbackQuery
            {
                Id = "query",
                Data = "old:1",
                From = new User { Id = 7 },
            },
            _client.Object,
            reason,
            typeof(Old)
        );

    private void Refuse(int code, string description) =>
        _client
            .Setup(x => x.SendRequest(It.IsAny<AnswerCallbackQueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException(description, code));

    private readonly record struct Old(int Id);
}
