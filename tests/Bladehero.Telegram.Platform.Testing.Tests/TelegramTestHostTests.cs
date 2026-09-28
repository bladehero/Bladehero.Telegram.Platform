using System.Diagnostics;
using Bladehero.Telegram.Platform.Receiving;
using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using FluentAssertions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed class TelegramTestHostTests
{
    [Fact]
    public async Task SendAsync_ShouldRunTheUpdateThroughTheRealPollingLoop()
    {
        // Arrange
        await using var bot = await StartBotAsync();

        // Act
        await bot.SendAsync(Text("hello"));

        // Assert
        bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage").Which.Parameters["text"]!
            .GetValue<string>()
            .Should()
            .Be("hello");
    }

    [Fact]
    public async Task SendAsync_ShouldReturnOnlyOnceTheBotHasFinishedTheUpdate()
    {
        // Arrange
        await using var bot = await StartBotAsync();

        // Act
        await bot.SendAsync(Text("/slow"));

        // Assert
        bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
    }

    [Fact]
    public async Task SendAsync_WhenACommandThrows_ShouldRethrowItsException()
    {
        // Arrange
        await using var bot = await StartBotAsync();

        // Act
        var act = () => bot.SendAsync(Text("/boom"));

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task SendAsync_AfterACommandThrew_ShouldKeepHandlingUpdates()
    {
        // Arrange
        await using var bot = await StartBotAsync();
        await ((Func<Task>)(() => bot.SendAsync(Text("/boom")))).Should().ThrowAsync<InvalidOperationException>();

        // Act
        await bot.SendAsync(Text("still here"));

        // Assert
        bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage");
    }

    [Fact]
    public async Task ForLongPollingAsync_ShouldRunTheHostsStartupAgainstTheFake()
    {
        // Act
        await using var bot = await StartBotAsync();

        // Assert
        bot.Api.Calls.Select(x => x.Method).Should().Contain("getWebhookInfo");
    }

    [Fact]
    public async Task SendAsync_WhenLongPollingIsNotRegistered_ShouldSayWhatIsMissing()
    {
        // Arrange
        await using var bot = await TelegramTestHost.ForLongPollingAsync(services =>
            services.AddTelegramReceiving(typeof(TelegramTestHostTests).Assembly)
        );
        bot.UpdateTimeout = TimeSpan.FromSeconds(1);

        // Act
        var act = () => bot.SendAsync(Text("hello"));

        // Assert
        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*AddTelegramLongPollingReceiving*");
    }

    [Fact]
    public async Task DisposeAsync_ShouldStopThePollingLoopWithoutWaitingOutItsLongPoll()
    {
        // Arrange
        var bot = await StartBotAsync();
        var stopwatch = Stopwatch.StartNew();

        // Act
        await bot.DisposeAsync();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    private static Task<TelegramTestHost> StartBotAsync() =>
        TelegramTestHost.ForLongPollingAsync(services =>
            services.AddTelegramLongPollingReceiving(
                receiver => receiver.Token = "unused",
                typeof(TelegramTestHostTests).Assembly
            )
        );

    private static Update Text(string text) =>
        new()
        {
            Message = new Message
            {
                Id = 1,
                Date = DateTime.UtcNow,
                Text = text,
                Chat = new Chat { Id = 42, Type = ChatType.Private },
                From = new User { Id = 42, FirstName = "Nick" },
            },
        };

    private sealed class EchoCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.Text?.StartsWith('/') is false);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(request.Payload.Chat, request.Payload.Text!, cancellationToken: token);
    }

    private sealed class SlowCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/slow"));

        protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), token);
            await request.Client.SendMessage(request.Payload.Chat, "done", cancellationToken: token);
        }
    }

    private sealed class BoomCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/boom"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            throw new InvalidOperationException("boom");
    }
}
