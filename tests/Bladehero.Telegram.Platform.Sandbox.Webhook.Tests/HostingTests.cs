using System.Globalization;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests;

// What the test host swaps in the app, in either receiving mode.
public sealed class HostingTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Update_WhenTheAppRegistersItsOwnBotClient_ShouldTalkToTheFake(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(
            mode,
            configure: web =>
                web.ConfigureTestServices(services =>
                    services.AddSingleton<ITelegramBotClient>(
                        new TelegramBotClient("7654321:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw")
                    )
                )
        );
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hello");
        await bot.Services.GetRequiredService<ITelegramBotClient>().SendMessage(nick.Chat.Id, "Your limit is near");

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal("Nick: hello", "Bot: Reply: hello [Again]", "Bot: Your limit is near");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Error_ShouldReachTheAppsOwnErrorHandlerWithItsUpdate(BotMode mode)
    {
        // Arrange
        var seen = new SeenErrors();
        await using var bot = await SandboxBot.StartAsync(
            mode,
            configure: web =>
                web.ConfigureTestServices(services =>
                {
                    services.AddSingleton(seen);
                    services.AddScoped<ITelegramErrorHandler, SeenErrorsHandler>();
                })
        );
        var nick = bot.PrivateChat("Nick");
        bot.Api.Fail("sendMessage", BotApiError.BotBlocked);

        // Act
        var thrown = await Record.ExceptionAsync(() => nick.SendsAsync("hello"));

        // Assert
        using (new AssertionScope())
        {
            thrown.Should().BeOfType<ApiRequestException>().Which.ErrorCode.Should().Be(403);
            var error = seen.All.Should().ContainSingle().Subject;
            error.Exception.Should().BeSameAs(thrown);
            error.Update!.Id.Should().Be(1);
            error.Update.Message!.Chat.Id.Should().Be(nick.Chat.Id);
        }
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Tap_WhenTheBotAsksOnlyForMessages_ShouldSayTelegramWouldNotSendIt(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(
            mode,
            configure: web => web.UseSetting("Telegram:AllowedUpdates:0", "Message")
        );
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAsync("hello");

        // Act
        var act = () => nick.TapsAsync("Again");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Telegram would not send this callback_query to the bot: it asked only for message "
                    + "(allowed_updates). Add UpdateType.CallbackQuery to AllowedUpdates."
            );
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Tap_AfterARestartThatNoLongerAsksOnlyForMessages_ShouldReachTheBot(BotMode mode)
    {
        // Arrange: the first deployment asked only for messages.
        var api = new FakeBotApi();
        await using (
            var first = await SandboxBot.StartAsync(
                mode,
                api,
                web => web.UseSetting("Telegram:AllowedUpdates:0", "Message")
            )
        )
        {
            await first.PrivateChat("Nick").SendsAsync("hello");
        }

        await using var bot = await SandboxBot.StartAsync(mode, api);
        var nick = bot.PrivateChat("Nick");

        // Act
        var answer = await nick.TapsAsync("Again");

        // Assert
        answer.ToString().Should().Be("Notification: Sent again");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Updates_ByTwoUsersAtOnce_ShouldEachRethrowOnlyTheirOwnError(BotMode mode)
    {
        // Arrange: Anna blocked the bot.
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");
        var anna = bot.PrivateChat("Anna");
        bot.Api.Fail("sendMessage", BotApiError.BotBlocked, chatId: anna.Chat.Id);
        var rounds = new List<string>();

        // Act
        for (var round = 0; round < 20; round++)
        {
            var outcomes = await Task.WhenAll(OutcomeOf(nick.SendsAsync("hello")), OutcomeOf(anna.SendsAsync("hi")));
            rounds.Add(string.Join(" | ", outcomes));
        }

        // Assert
        rounds.Should().AllBeEquivalentTo("no error | 403");
    }

    private static async Task<string> OutcomeOf(Task action)
    {
        try
        {
            await action;
            return "no error";
        }
        catch (ApiRequestException exception)
        {
            return exception.ErrorCode.ToString(CultureInfo.InvariantCulture);
        }
    }

    // The app's own error handler, and what it saw.
    private sealed class SeenErrors
    {
        private readonly List<TelegramError> _all = [];

        public IReadOnlyList<TelegramError> All
        {
            get
            {
                lock (_all)
                {
                    return [.. _all];
                }
            }
        }

        public void Add(TelegramError error)
        {
            lock (_all)
            {
                _all.Add(error);
            }
        }
    }

    private sealed class SeenErrorsHandler(SeenErrors seen) : ITelegramErrorHandler
    {
        public Task HandleAsync(TelegramError telegramError)
        {
            seen.Add(telegramError);
            return Task.CompletedTask;
        }
    }
}
