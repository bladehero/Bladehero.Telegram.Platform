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
    public async Task SendsAsync_ShouldReturnTheMessageAsPosted()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var sent = await nick.SendsAsync(" hello ");

        // Assert
        using (new AssertionScope())
        {
            sent.Id.Should().Be(nick.Messages[0].Id);
            sent.Text.Should().Be("hello");
            sent.Message.From!.Id.Should().Be(nick.Id);
        }
    }

    [Fact]
    public async Task SendsAsync_WhenTheBotDeletesTheMessage_ShouldStillReturnIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var sent = await nick.SendsAsync("/tidy");

        // Assert
        using (new AssertionScope())
        {
            sent.Text.Should().Be("/tidy");
            nick.Messages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SendsPhotoAsync_ShouldReturnTheMessageWithItsPhoto()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var sent = await nick.SendsPhotoAsync("jpeg bytes"u8.ToArray(), caption: "Lunch");

        // Assert
        using (new AssertionScope())
        {
            sent.Caption.Should().Be("Lunch");
            sent.Photo!.Content.Should().Equal("jpeg bytes"u8.ToArray());
        }
    }

    [Fact]
    public async Task SendsAlbumAsync_ShouldSendEachPhotoAsItsOwnUpdateInOneGroup()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var sent = await nick.SendsAlbumAsync(["one"u8.ToArray(), "two"u8.ToArray(), "three"u8.ToArray()], "Lunch");

        // Assert
        using (new AssertionScope())
        {
            sent.Select(x => x.Photo!.ReadAsString()).Should().Equal("one", "two", "three");
            sent.Select(x => x.Caption).Should().Equal("Lunch", null, null);
            sent.Select(x => x.Message.MediaGroupId).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
            sent.Select(x => x.Id).Should().BeInAscendingOrder();
            AlbumReplies(nick).Should().Equal("Album item: Lunch", "Album item: no caption", "Album item: no caption");
        }
    }

    [Fact]
    public async Task SendsDocumentAlbumAsync_ShouldPutTheCaptionUnderTheLastFile()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var sent = await nick.SendsDocumentAlbumAsync(
            [("a,b"u8.ToArray(), "march.csv"), ("%PDF"u8.ToArray(), "april.pdf")],
            "Reports"
        );

        // Assert
        using (new AssertionScope())
        {
            sent.Select(x => x.Document!.FileName).Should().Equal("march.csv", "april.pdf");
            sent.Select(x => x.Document!.MimeType).Should().Equal("text/csv", "application/pdf");
            sent.Select(x => x.Caption).Should().Equal(null, "Reports");
            sent.Select(x => x.Message.MediaGroupId).Distinct().Should().ContainSingle();
            AlbumReplies(nick).Should().Equal("Album item: no caption", "Album item: Reports");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public async Task SendsAlbumAsync_WithoutTwoToTenPhotos_ShouldBeRefused(int count)
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAlbumAsync([.. Enumerable.Repeat("jpeg"u8.ToArray(), count)]);

        // Assert
        using (new AssertionScope())
        {
            await act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage($"An album holds 2 to 10 items, not {count}.*");
            nick.Messages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SendsAlbumAsync_WithAnEmptyPhoto_ShouldBeRefused()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () => nick.SendsAlbumAsync(["jpeg"u8.ToArray(), []]);

        // Assert
        using (new AssertionScope())
        {
            await act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage("The Telegram app never sends an empty file.*");
            nick.Messages.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SendsDocumentAlbumAsync_WhenTheSecondItemFails_ShouldSendNoMore()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        var act = () =>
            nick.SendsDocumentAlbumAsync([
                ("a"u8.ToArray(), "first.csv"),
                ("b"u8.ToArray(), "broken.csv"),
                ("c"u8.ToArray(), "third.csv"),
            ]);

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cannot read broken.csv");
            nick.Messages.Where(x => !x.IsFromBot)
                .Select(x => x.Message.Document!.FileName)
                .Should()
                .Equal("first.csv", "broken.csv");
        }
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
    public async Task TapsAsync_ByPredicate_ShouldPressTheButtonItPicks()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/card");

        // Act
        var answer = await nick.TapsAsync(b => b.CallbackData == "date:next");

        // Assert
        answer.ToString().Should().Be("Notification: moved date next");
    }

    [Fact]
    public async Task TapsAsync_ByAPredicateMatchingTwoButtons_ShouldSayItIsAmbiguous()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/card");

        // Act
        var act = () => nick.TapsAsync(b => b.Text == "◀");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "\"Card\" shows more than one button matching the predicate, so which one Nick taps is ambiguous.*"
            );
    }

    [Fact]
    public async Task TapsAsync_ByAPredicateMatchingNothing_ShouldNameTheButtons()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/card");

        // Act
        var act = () => nick.TapsAsync(b => b.CallbackData == "year:back");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Nick sees no button matching the predicate in the chat with Nick. The buttons are \"◀\", \"▶\"."
            );
    }

    [Fact]
    public async Task TapsAsync_WithADuplicatedLabel_ShouldPointToThePredicateOverload()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("/card");

        // Act
        var act = () => nick.TapsAsync("◀");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "\"Card\" shows more than one \"◀\" button, so which one Nick taps is ambiguous. Pick one with "
                    + "TapsAsync(b => b.CallbackData == \"method:back\")."
            );
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

    // The bot's answers to album items only; its file command answers them too.
    private static IEnumerable<string?> AlbumReplies(TestUser user) =>
        user
            .Messages.Where(x => x.Text?.StartsWith("Album item:", StringComparison.Ordinal) is true)
            .Select(x => x.Text);
}
