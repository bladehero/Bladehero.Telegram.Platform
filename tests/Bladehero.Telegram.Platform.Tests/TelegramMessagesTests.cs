using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.ReplyMarkups;
using static Bladehero.Telegram.Platform.Tests.ScriptedBotClient;

namespace Bladehero.Telegram.Platform.Tests;

public sealed class TelegramMessagesTests
{
    private const long Nick = 7000000001;
    private const string NotModified =
        "message is not modified: specified new message content and reply markup are exactly the same as a current "
        + "content and reply markup of the message";

    private static readonly TelegramMessageRef Card = new(Nick, 5);
    private static readonly TelegramMessageRef Inline = new("AAAAinline");
    private static readonly InlineKeyboardMarkup Keyboard = new InlineKeyboardMarkup().AddButton("Redeem 10", "r:10");

    private readonly ScriptedBotClient _telegram = new();
    private readonly LogRecorder _logs = new();
    private readonly TelegramMessages _sut;

    public TelegramMessagesTests() =>
        _sut = new TelegramMessages(_telegram, new LoggerFactory([_logs]).CreateLogger<TelegramMessages>());

    [Fact]
    public async Task SendAsync_ShouldReturnTheMessageSent()
    {
        // Act
        var sent = await _sut.SendAsync(Nick, "You have 50 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            sent.Should().Be(new TelegramMessageRef(Nick, 10));
            _telegram
                .Calls.Should()
                .ContainSingle()
                .Which.Should()
                .Be(new Call("sendMessage", Nick, null, null, "You have 50 points.", true));
        }
    }

    public static TheoryData<ReplyMarkup> ReplyMarkups =>
        [new ReplyKeyboardMarkup("Latte", "Cappuccino"), new ForceReplyMarkup(), new ReplyKeyboardRemove()];

    [Theory]
    [MemberData(nameof(ReplyMarkups))]
    public async Task SendAsync_WithAReplyKeyboard_ShouldSendIt(ReplyMarkup markup)
    {
        // Act
        var sent = await _sut.SendAsync(Nick, "Which coffee?", markup);

        // Assert
        using (new AssertionScope())
        {
            sent.Should().Be(new TelegramMessageRef(Nick, 10));
            _telegram.Sent<SendMessageRequest>().Should().ContainSingle().Which.ReplyMarkup.Should().BeSameAs(markup);
        }
    }

    [Fact]
    public async Task ShowAsync_WhenTheEditSucceeds_ShouldKeepTheMessage()
    {
        // Act
        var shown = await _sut.ShowAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().Be(Card);
            _telegram
                .Calls.Should()
                .ContainSingle()
                .Which.Should()
                .Be(new Call("editMessageText", Nick, 5, null, "You have 40 points.", true));
        }
    }

    [Fact]
    public async Task ShowAsync_WhenNotModified_ShouldKeepTheMessage()
    {
        // Arrange
        _telegram.Answer("editMessageText", Refusal(NotModified));

        // Act
        var shown = await _sut.ShowAsync(Card, "You have 50 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().Be(Card);
            _telegram.Methods.Should().Equal("editMessageText");
        }
    }

    [Theory]
    [InlineData("message to edit not found")]
    [InlineData("MESSAGE_ID_INVALID")]
    [InlineData("message not found")]
    public async Task ShowAsync_WhenTheMessageIsGone_ShouldSendAFreshOne(string refusal)
    {
        // Arrange
        _telegram.Answer("editMessageText", Refusal(refusal));

        // Act
        var shown = await _sut.ShowAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().Be(new TelegramMessageRef(Nick, 10));
            _telegram.Methods.Should().Equal("editMessageText", "sendMessage");
        }
    }

    [Theory]
    [InlineData("message can't be edited")]
    [InlineData("there is no text in the message to edit")]
    public async Task ShowAsync_WhenTheMessageCantBeEdited_ShouldSendAFreshOneThenDeleteTheOld(string refusal)
    {
        // Arrange
        _telegram.Answer("editMessageText", Refusal(refusal));

        // Act
        var shown = await _sut.ShowAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().Be(new TelegramMessageRef(Nick, 10));
            _telegram.Methods.Should().Equal("editMessageText", "sendMessage", "deleteMessage");
            _telegram.Calls[^1].MessageId.Should().Be(5);
        }
    }

    [Fact]
    public async Task ShowAsync_WhenTheOldOneCantBeRemoved_ShouldReturnTheFreshOneAndWarn()
    {
        // Arrange
        _telegram
            .Answer("editMessageText", Refusal("message can't be edited"))
            .Answer("deleteMessage", new RequestException("Exception during making request"));

        // Act
        var shown = await _sut.ShowAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().Be(new TelegramMessageRef(Nick, 10));
            _logs
                .At(LogLevel.Warning)
                .Should()
                .Equal($"Couldn't remove message 5 in chat {Nick}; a fresh one replaced it.");
        }
    }

    [Fact]
    public async Task ShowAsync_WhenTheUserBlockedTheBot_ShouldThrowAndCallNothingElse()
    {
        // Arrange
        _telegram.Answer("editMessageText", Refusal("Forbidden: bot was blocked by the user", 403));

        // Act
        var act = () => _sut.ShowAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            (await act.Should().ThrowAsync<ApiRequestException>()).Which.ErrorCode.Should().Be(403);
            _telegram.Methods.Should().Equal("editMessageText");
        }
    }

    [Fact]
    public async Task ShowAsync_WhenRateLimited_ShouldThrow()
    {
        // Arrange
        _telegram.Answer("editMessageText", Refusal("Too Many Requests: retry after 5", 429));

        // Act
        var act = () => _sut.ShowAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.ErrorCode.Should()
            .Be(429);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowAsync_OfAnInlineMessage_ShouldKeepIt(bool notModified)
    {
        // Arrange
        if (notModified)
        {
            _telegram.Answer("editMessageText", Refusal(NotModified));
        }

        // Act
        var shown = await _sut.ShowAsync(Inline, "Done", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            shown.Should().Be(Inline);
            _telegram
                .Calls.Should()
                .ContainSingle()
                .Which.Should()
                .Be(new Call("editMessageText", null, null, "AAAAinline", "Done", true));
        }
    }

    [Fact]
    public async Task ShowAsync_OfAGoneInlineMessage_ShouldThrow()
    {
        // Arrange
        _telegram.Answer("editMessageText", Refusal("MESSAGE_ID_INVALID"));

        // Act
        var act = () => _sut.ShowAsync(Inline, "Done", Keyboard);

        // Assert
        await act.Should().ThrowAsync<ApiRequestException>().WithMessage("*MESSAGE_ID_INVALID");
    }

    [Fact]
    public async Task ReplaceAsync_ShouldSendTheFreshOneBeforeDeletingTheOld()
    {
        // Act
        var replaced = await _sut.ReplaceAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            replaced.Should().Be(new TelegramMessageRef(Nick, 10));
            _telegram.Methods.Should().Equal("sendMessage", "deleteMessage");
            _telegram.Calls[1].MessageId.Should().Be(5);
        }
    }

    [Fact]
    public async Task ReplaceAsync_WhenTheOldOneIsGone_ShouldReturnTheFreshOne()
    {
        // Arrange
        _telegram.Answer("deleteMessage", Refusal("message to delete not found"));

        // Act
        var replaced = await _sut.ReplaceAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            replaced.Should().Be(new TelegramMessageRef(Nick, 10));
            _logs.Entries.Should().NotContain(x => x.Level >= LogLevel.Warning);
        }
    }

    [Fact]
    public async Task ReplaceAsync_WhenTheOldOneCantBeDeleted_ShouldClearItsKeyboard()
    {
        // Arrange
        _telegram.Answer("deleteMessage", Refusal("message can't be deleted for everyone"));

        // Act
        var replaced = await _sut.ReplaceAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            replaced.Should().Be(new TelegramMessageRef(Nick, 10));
            _telegram.Methods.Should().Equal("sendMessage", "deleteMessage", "editMessageReplyMarkup");
            _telegram.Calls[^1].Should().Be(new Call("editMessageReplyMarkup", Nick, 5, null, null, false));
        }
    }

    [Fact]
    public async Task ReplaceAsync_CancelledAfterTheFreshSend_ShouldStillRemoveTheOldAndReturnTheFresh()
    {
        // Arrange: the caller gives up while Telegram answers the send.
        using var cancellation = new CancellationTokenSource();
        _telegram.OnCall("sendMessage", cancellation.Cancel);

        // Act
        var replaced = await _sut.ReplaceAsync(Card, "You have 40 points.", Keyboard, token: cancellation.Token);

        // Assert
        using (new AssertionScope())
        {
            replaced.Should().Be(new TelegramMessageRef(Nick, 10));
            _telegram.Methods.Should().Equal("sendMessage", "deleteMessage");
        }
    }

    [Fact]
    public async Task ReplaceAsync_WhenTheSendFails_ShouldThrowAndKeepTheOld()
    {
        // Arrange
        _telegram.Answer("sendMessage", Refusal("Forbidden: bot was blocked by the user", 403));

        // Act
        var act = () => _sut.ReplaceAsync(Card, "You have 40 points.", Keyboard);

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<ApiRequestException>();
            _telegram.Methods.Should().Equal("sendMessage");
        }
    }

    [Fact]
    public async Task ReplaceAsync_OfAnInlineMessage_ShouldThrow()
    {
        // Act
        var act = () => _sut.ReplaceAsync(Inline, "Done");

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("message");
            _telegram.Calls.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("message to delete not found")]
    [InlineData("MESSAGE_ID_INVALID")]
    [InlineData("message not found")]
    public async Task DeleteAsync_WhenDeletedOrAlreadyGone_ShouldReturnTrue(string? refusal)
    {
        // Arrange
        if (refusal is not null)
        {
            _telegram.Answer("deleteMessage", Refusal(refusal));
        }

        // Act
        var deleted = await _sut.DeleteAsync(Card);

        // Assert
        using (new AssertionScope())
        {
            deleted.Should().BeTrue();
            _telegram.Methods.Should().Equal("deleteMessage");
        }
    }

    [Fact]
    public async Task DeleteAsync_WhenItCantBeDeleted_ShouldClearTheKeyboardAndReturnFalse()
    {
        // Arrange
        _telegram.Answer("deleteMessage", Refusal("message can't be deleted"));

        // Act
        var deleted = await _sut.DeleteAsync(Card);

        // Assert
        using (new AssertionScope())
        {
            deleted.Should().BeFalse();
            _telegram.Methods.Should().Equal("deleteMessage", "editMessageReplyMarkup");
        }
    }

    [Fact]
    public async Task DeleteAsync_OfAnInlineMessage_ShouldClearItsKeyboardAndReturnFalse()
    {
        // Act
        var deleted = await _sut.DeleteAsync(Inline);

        // Assert
        using (new AssertionScope())
        {
            deleted.Should().BeFalse();
            _telegram
                .Calls.Should()
                .ContainSingle()
                .Which.Should()
                .Be(new Call("editMessageReplyMarkup", null, null, "AAAAinline", null, false));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenItCanBeNeitherDeletedNorEdited_ShouldReturnFalse(bool inline)
    {
        // Arrange
        _telegram
            .Answer("deleteMessage", Refusal("message can't be deleted"))
            .Answer("editMessageReplyMarkup", Refusal("message can't be edited"));

        // Act
        var deleted = await _sut.DeleteAsync(inline ? Inline : Card);

        // Assert
        using (new AssertionScope())
        {
            deleted.Should().BeFalse();
            _telegram.Methods.Should().EndWith("editMessageReplyMarkup");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(NotModified)]
    [InlineData("message to edit not found")]
    [InlineData("message not found")]
    public async Task ClearKeyboardAsync_WhenClearedUnchangedOrGone_ShouldNotThrow(string? refusal)
    {
        // Arrange
        if (refusal is not null)
        {
            _telegram.Answer("editMessageReplyMarkup", Refusal(refusal));
        }

        // Act
        var act = () => _sut.ClearKeyboardAsync(Card);

        // Assert
        using (new AssertionScope())
        {
            await act.Should().NotThrowAsync();
            _telegram
                .Calls.Should()
                .ContainSingle()
                .Which.Should()
                .Be(new Call("editMessageReplyMarkup", Nick, 5, null, null, false));
        }
    }

    [Fact]
    public async Task ClearKeyboardAsync_WhenItCantBeEdited_ShouldThrow()
    {
        // Arrange
        _telegram.Answer("editMessageReplyMarkup", Refusal("message can't be edited"));

        // Act
        var act = () => _sut.ClearKeyboardAsync(Card);

        // Assert
        await act.Should().ThrowAsync<ApiRequestException>();
    }

    [Fact]
    public async Task AnyMethod_WithADefaultRef_ShouldThrowArgumentException()
    {
        // Arrange
        Func<Task>[] calls =
        [
            () => _sut.ShowAsync(default, "x"),
            () => _sut.ReplaceAsync(default, "x"),
            () => _sut.DeleteAsync(default),
            () => _sut.ClearKeyboardAsync(default),
        ];

        // Act
        foreach (var call in calls)
        {
            // Assert
            await call.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage("The message reference names no message.*")
                .WithParameterName("message");
        }

        _telegram.Calls.Should().BeEmpty();
    }
}
