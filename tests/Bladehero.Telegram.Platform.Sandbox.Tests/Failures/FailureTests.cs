using Bladehero.Telegram.Platform.Sandbox.Receipts;
using Bladehero.Telegram.Platform.Sandbox.Tests.Receipts;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Failures;

public sealed class FailureTests
{
    private const string Apology = "Sorry, something went wrong — please try again.";

    [Fact]
    public async Task Receipt_WhenTheReaderThrows_ShouldFailTheActionAndApologise()
    {
        // Arrange
        var reader = new ScriptedReceiptReader { Failure = new InvalidOperationException("The reader is down.") };
        await using var bot = await SandboxBot.StartWithMembersAsync(
            SandboxBot.Using<IReceiptReader>(reader),
            ("Nick", 0)
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        var error = await Record.ExceptionAsync(() => nick.SendsPhotoAsync("a receipt from Luigi's"u8.ToArray()));

        // Assert: the test host records the error and hands it on to the app's own handler.
        using (new AssertionScope())
        {
            error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("The reader is down.");
            nick.LastMessage.Text.Should().Be(Apology);
        }
    }

    [Fact]
    public async Task Anna_WhoBlockedTheBot_ShouldNotStopNicksService()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var anna = bot.PrivateChat("Anna");
        var nick = bot.PrivateChat("Nick");
        bot.Api.Fail("sendMessage", BotApiError.BotBlocked, chatId: anna.Chat.Id);

        // Act
        var annasError = await Record.ExceptionAsync(() => anna.SendsAsync("/start"));
        await nick.SendsAsync("/start");

        // Assert: the apology to Anna failed too, and was swallowed rather than wrapped with her error.
        var replies = nick.Messages.Where(x => x.IsFromBot).Select(x => x.Text!).ToArray();
        using (new AssertionScope())
        {
            annasError.Should().BeOfType<ApiRequestException>().Which.ErrorCode.Should().Be(403);
            bot.Api.Calls.Where(x =>
                    x.Method == "sendMessage" && x.Parameters["chat_id"]!.GetValue<long>() == anna.Chat.Id
                )
                .Select(x => x.Parameters["text"]!.GetValue<string>())
                .Should()
                .Contain(Apology);
            replies.Should().HaveCount(3);
            replies[0].Should().Be("Welcome to the coffee shop, Nick!");
            replies[1].Should().StartWith("Here is what I can do:");
            replies[2].Should().Be("Tip: members earn 10 points a coffee — /join");
        }
    }

    [Fact]
    public async Task Reply_AfterTooManyRequestsOnce_ShouldStillArrive()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");
        bot.Api.Fail("sendMessage", BotApiError.TooManyRequests(1), times: 1);

        // Act
        await nick.SendsAsync("/help");

        // Assert: Telegram.Bot waited the second Telegram asked for, and sent it again.
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().StartWith("Here is what I can do:");
            bot.Api.Calls.Where(x => x.Method == "sendMessage").Should().HaveCount(2);
        }
    }
}
