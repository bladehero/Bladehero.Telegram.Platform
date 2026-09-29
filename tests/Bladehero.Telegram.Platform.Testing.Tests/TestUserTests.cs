using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class TestUserTests
{
    [Fact]
    public async Task SendsAsync_ShouldReachTheBotFromTheUser()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/whoami");

        // Assert
        nick.LastMessage.Text.Should().Be("You are Nick");
    }

    [Fact]
    public async Task SendsAsync_WhenTheBotAsksOnlyForMessages_ShouldReachIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(receiver: receiver =>
            receiver.AllowedUpdates = [UpdateType.Message]
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/whoami");

        // Assert
        nick.LastMessage.Text.Should().Be("You are Nick");
    }

    [Fact]
    public async Task TapsAsync_WhenTheBotAsksOnlyForMessages_ShouldSayTelegramWouldNotSendTheTap()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(receiver: receiver =>
            receiver.AllowedUpdates = [UpdateType.Message]
        );
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var before = nick.Messages.Select(x => x.ToString()).ToArray();

        // Act
        var act = () => nick.TapsAsync("A");

        // Assert
        using (new AssertionScope())
        {
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage(
                    "Telegram would not send this callback_query to the bot: it asked only for message "
                        + "(allowed_updates). Add UpdateType.CallbackQuery to AllowedUpdates."
                );
            nick.Messages.Select(x => x.ToString()).Should().Equal(before);
        }
    }

    [Fact]
    public async Task TapsAsync_AfterARestartThatNoLongerSetsAllowedUpdates_ShouldReachTheBot()
    {
        // Arrange: the first deployment asked only for messages.
        var api = new FakeBotApi();
        await using (
            var first = await TestBot.StartAsync(
                api,
                receiver: receiver => receiver.AllowedUpdates = [UpdateType.Message]
            )
        )
        {
            await first.PrivateChat("Nick").SendsAsync("/menu");
        }

        await using var bot = await TestBot.StartAsync(api);
        var nick = bot.PrivateChat("Nick");

        // Act
        var answer = await nick.TapsAsync("A");

        // Assert
        answer.ToString().Should().Be("Notification: You picked A");
    }

    [Fact]
    public async Task TapsAsync_WithDropPendingUpdates_ShouldUseTheListTheLoopAskedFor()
    {
        // Arrange: the tap is the first update, just after the loop dropped pending ones with an empty list.
        await using var bot = await TestBot.StartAsync(receiver: receiver =>
        {
            receiver.DropPendingUpdates = true;
            receiver.AllowedUpdates = [UpdateType.Message];
        });
        var nick = bot.PrivateChat("Nick");
        await bot
            .Api.CreateClient()
            .SendMessage(
                nick.Chat.Id,
                "Pick one",
                replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("A", "pick:A"))
            );

        // Act
        var act = () => nick.TapsAsync("A");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Telegram would not send this callback_query to the bot: it asked only for message *");
    }

    [Fact]
    public async Task SendsAsync_WithTextOverTelegramsLimit_ShouldSayTheAppSplitsIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAsync(new string('a', 4097));

        // Assert
        using (new AssertionScope())
        {
            await act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage(
                    "Telegram takes at most 4096 characters in a message, and this text has 4097: the Telegram app "
                        + "splits longer text into several messages; send them one by one.*"
                );
            nick.Messages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SendsPhotoAsync_WithACaptionOverTelegramsLimit_ShouldRefuseIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsPhotoAsync("jpeg"u8.ToArray(), caption: new string('a', 1025));

        // Assert
        using (new AssertionScope())
        {
            await act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage("Telegram takes at most 1024 characters in a caption, and this one has 1025.*");
            nick.Messages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SendsAsync_ShouldAddTheMessageToTheChatBeforeTheBotsReply()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: hello", "Bot: hello");
    }

    [Fact]
    public async Task SendsAsync_WithABotCommand_ShouldMarkItTheWayTelegramDoes()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/whoami please");

        // Assert
        var entity = nick.Messages[0].Message.Entities.Should().ContainSingle().Which;
        using (new AssertionScope())
        {
            entity.Type.Should().Be(MessageEntityType.BotCommand);
            entity.Offset.Should().Be(0);
            entity.Length.Should().Be("/whoami".Length);
        }
    }

    [Theory]
    [InlineData("/a-b", "/a")]
    [InlineData("/кофе", null)]
    [InlineData("/start@my_bot now", "/start@my_bot")]
    public async Task SendsAsync_WithABotCommand_ShouldMarkOnlyWhatACommandMayHold(string text, string? marked)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync(text);

        // Assert
        nick.Messages[0]
            .Message.Entities?.Select(x => text.Substring(x.Offset, x.Length))
            .SingleOrDefault()
            .Should()
            .Be(marked);
    }

    [Fact]
    public async Task SendsAsync_ByAGroupMember_ShouldReachTheBotFromThatMember()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var anna = bot.GroupChat("Family").Member("Anna");

        // Act
        await anna.SendsAsync("/whoami");

        // Assert
        anna.LastMessage.Text.Should().Be("You are Anna");
    }

    [Fact]
    public async Task SendsPhotoAsync_ShouldLetTheBotDownloadThePhoto()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync("not really a jpeg"u8.ToArray());

        // Assert
        nick.LastMessage.Text.Should().Be("Got a photo: not really a jpeg");
    }

    [Fact]
    public async Task SendsPhotoAsync_WithACaption_ShouldShowItUnderThePhoto()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync("..."u8.ToArray(), caption: "Lunch");

        // Assert
        using (new AssertionScope())
        {
            nick.Messages[0].Caption.Should().Be("Lunch");
            nick.Messages[0].ToString().Should().Be("Nick: (photo) Lunch");
        }
    }

    [Fact]
    public async Task SendsVoiceAsync_ShouldLetTheBotDownloadTheRecording()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsVoiceAsync("hello"u8.ToArray(), TimeSpan.FromSeconds(3));

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: (voice 3s)", "Bot: Got 3s of voice: hello");
    }

    [Fact]
    public async Task SendsDocumentAsync_ShouldWorkOutTheTypeFromTheFileName()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync("a,b"u8.ToArray(), "notes.csv");

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal("Nick: (document notes.csv)", "Bot: Got notes.csv as text/csv: a,b");
    }

    [Theory]
    [InlineData("sticker.webp", "image/webp")]
    [InlineData("loop.gif", "image/gif")]
    [InlineData("IMG_0001.HEIC", "image/heic")]
    [InlineData("note.ogg", "audio/ogg")]
    [InlineData("note.oga", "audio/ogg")]
    [InlineData("song.mp3", "audio/mpeg")]
    [InlineData("clip.mp4", "video/mp4")]
    public async Task SendsDocumentAsync_WithAMediaFile_ShouldWorkOutItsType(string fileName, string mimeType)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync("bytes"u8.ToArray(), fileName);

        // Assert
        nick.Messages[0].Message.Document!.MimeType.Should().Be(mimeType);
    }

    [Fact]
    public async Task SendsDocumentAsync_WithAMimeType_ShouldSendThatType()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync("{}"u8.ToArray(), "budget", mimeType: "application/json");

        // Assert
        nick.LastMessage.Text.Should().Be("Got budget as application/json: {}");
    }

    [Theory]
    [InlineData("звіт за травень.pdf")]
    [InlineData("report.final version")]
    [InlineData("v1.0#final")]
    [InlineData("q.a?b")]
    [InlineData("x.%41")]
    public async Task SendsDocumentAsync_WhateverTheFileIsNamed_ShouldLetTheBotDownloadIt(string fileName)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync("x"u8.ToArray(), fileName, mimeType: "text/plain");

        // Assert
        nick.LastMessage.Text.Should().Be($"Got {fileName} as text/plain: x");
    }

    [Fact]
    public async Task SendsDocumentAsync_ByAGroupMember_ShouldReachTheBotInTheGroup()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var family = bot.GroupChat("Family");

        // Act
        await family.Member("Anna").SendsDocumentAsync("a,b"u8.ToArray(), "budget.csv");

        // Assert
        family
            .Messages.Select(x => x.ToString())
            .Should()
            .Equal("Anna: (document budget.csv)", "Bot: Got budget.csv as text/csv: a,b");
    }

    [Fact]
    public async Task SendsPhotoAsync_WithABotCommandInTheCaption_ShouldMarkItTheWayTelegramDoes()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync("..."u8.ToArray(), caption: "/receipt lunch");

        // Assert
        var entity = nick.Messages[0].Message.CaptionEntities.Should().ContainSingle().Which;
        using (new AssertionScope())
        {
            entity.Type.Should().Be(MessageEntityType.BotCommand);
            entity.Length.Should().Be("/receipt".Length);
        }
    }

    [Fact]
    public async Task SendsAsync_ShouldTrimTheTextAsTelegramDoes()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("  hello \n");

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: hello", "Bot: hello");
    }

    [Fact]
    public async Task SendsPhotoAsync_ShouldCarryAThumbnailFirstAndThePhotoLast()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync("lunch"u8.ToArray());

        // Assert
        nick.Messages[0].Message.Photo!.Select(x => x.Width).Should().BeInAscendingOrder().And.HaveCount(2);
    }

    [Fact]
    public async Task SendsPhotoAsync_ShouldTrimTheCaptionAsTelegramDoes()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync("..."u8.ToArray(), caption: " Lunch ");

        // Assert
        nick.Messages[0].ToString().Should().Be("Nick: (photo) Lunch");
    }

    [Fact]
    public async Task SendsDocumentAsync_WithABlankMimeType_ShouldWorkOutTheTypeFromTheFileName()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync("a,b"u8.ToArray(), "notes.csv", mimeType: " ");

        // Assert
        nick.LastMessage.Text.Should().Be("Got notes.csv as text/csv: a,b");
    }

    [Fact]
    public async Task SendsPhotoAsync_WithAnEmptyCaption_ShouldSendNone()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync("..."u8.ToArray(), caption: " ");

        // Assert
        nick.Messages[0].Caption.Should().BeNull();
    }

    [Fact]
    public async Task SendsPhotoAsync_WithNoBytes_ShouldSayTheAppNeverSendsAnEmptyFile()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsPhotoAsync([]);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*never sends an empty file*");
    }

    [Fact]
    public async Task SendsVoiceAsync_WithANegativeDuration_ShouldRefuseIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsVoiceAsync("hello"u8.ToArray(), TimeSpan.FromSeconds(-5));

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SendsAsync_WhenTelegramRefusesTheBotsReply_ShouldRethrowTheError()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        bot.Api.Fail("sendMessage", BotApiError.BotBlocked);

        // Act
        var act = () => nick.SendsAsync("/whoami");

        // Assert
        (await act.Should().ThrowAsync<ApiRequestException>())
            .Which.ErrorCode.Should()
            .Be(403);
    }

    [Fact]
    public async Task TapsAsync_ShouldReturnTheNotificationTheBotAnsweredWith()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        var answer = await nick.TapsAsync("A");

        // Assert
        answer.ToString().Should().Be("Notification: You picked A");
    }

    [Fact]
    public async Task TapsAsync_WhenTheBotAnswersWithAnAlert_ShouldSaySo()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        var answer = await nick.TapsAsync("B");

        // Assert
        answer.IsAlert.Should().BeTrue();
    }

    [Fact]
    public async Task TapsAsync_WhenTheBotNeverAnswers_ShouldSaySo()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        var answer = await nick.TapsAsync("Ignore");

        // Assert
        answer.IsAnswered.Should().BeFalse();
    }

    [Fact]
    public async Task TapsAsync_ShouldPressTheButtonTheBotSent()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        await nick.TapsAsync("B");

        // Assert
        nick.LastMessage.ToString().Should().Be("Bot: Nick picked B");
    }

    [Fact]
    public async Task TapsAsync_ShouldPressTheButtonOnTheNewestMessageShowingIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        await nick.SendsAsync("/menu");

        // Act
        await nick.TapsAsync("A");

        // Assert
        nick.Messages.Where(x => x.IsFromBot).Select(x => x.Text).Should().Equal("Pick one", "Nick picked A");
    }

    [Fact]
    public async Task TapsAsync_OnAnOlderMessage_ShouldPressTheButtonThere()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var first = nick.LastMessage;
        await nick.SendsAsync("/menu");

        // Act
        await nick.TapsAsync("A", on: first);

        // Assert
        nick.Messages.Where(x => x.IsFromBot).Select(x => x.Text).Should().Equal("Nick picked A", "Pick one");
    }

    [Fact]
    public async Task TapsAsync_WhenNoMessageShowsTheButton_ShouldNameTheButtonsThatAre()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        var act = () => nick.TapsAsync("C");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Nick sees no \"C\" button*The buttons are \"A\", \"B\", \"Dismiss\", \"Ignore\", \"Docs\".");
    }

    [Fact]
    public async Task TapsAsync_OnALink_ShouldSayTheBotNeverHearsOfIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");

        // Act
        var act = () => nick.TapsAsync("Docs");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*\"Docs\"*not a callback button*");
    }

    [Fact]
    public async Task TapsAsync_OnAMessageTheBotDeleted_ShouldSaySo()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/menu");
        var menu = nick.LastMessage;
        await nick.TapsAsync("Dismiss");

        // Act
        var act = () => nick.TapsAsync("A", on: menu);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*\"Pick one\" is no longer in*");
    }

    [Fact]
    public async Task TapsAsync_OnAMessageWithoutText_ShouldQuoteItAsTheChatShowsIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsPhotoAsync("..."u8.ToArray());

        // Act
        var act = () => nick.TapsAsync("A", on: nick.Messages[0]);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Nick sees no \"A\" button on \"(photo)\"*");
    }
}
