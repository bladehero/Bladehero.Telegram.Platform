using Bladehero.Telegram.Platform.History;
using Bladehero.Telegram.Platform.History.InMemory;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class TelegramTestHostTests
{
    private const string OwnToken = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    [Fact]
    public async Task History_AfterAnAction_ShouldHoldTheUpdateAndTheReply()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: WithHistory);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hi");

        // Assert
        var entries = await bot.History.ReadAsync(new() { ChatId = nick.Chat.Id });
        using (new AssertionScope())
        {
            entries.Select(x => x.ToString()).Should().Equal("message: hi", "sendMessage: hi");
            entries.Select(x => x.UpdateId).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task History_OfAMessageSentLater_ShouldBeReadableOnceItArrives()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: WithHistory);
        var nick = bot.PrivateChat("Nick");
        var trigger = await nick.SendsAsync("/later");

        // Act
        await nick.WaitForMessageAsync(x => x.Text == "later", after: trigger);

        // Assert
        var entries = await bot.History.ReadAsync(new() { ChatId = nick.Chat.Id });
        entries
            .Should()
            .ContainSingle(x => x.Kind == "sendMessage")
            .Which.Should()
            .BeEquivalentTo(new { Text = "later", entries.Single(x => x.Kind == "message").UpdateId });
    }

    [Fact]
    public async Task History_WithoutAddTelegramHistory_ShouldThrowNamingIt()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync();

        // Act
        var act = () => bot.History;

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddTelegramHistory()*");
    }

    [Fact]
    public async Task Action_ShouldWaitUntilTheHistoryIsStored()
    {
        // Arrange
        var store = new GatedStore();
        await using var bot = await TestBot.StartAsync(services: s =>
            s.AddTelegramHistory().Services.AddSingleton<ITelegramHistoryStore>(store)
        );
        var nick = bot.PrivateChat("Nick");
        await bot.History.FlushAsync();
        store.Close();
        var action = nick.SendsAsync("hi");
        await store.Entered;
        var whileStoring = action.IsCompleted;

        // Act
        store.Open();
        await action;

        // Assert
        whileStoring.Should().BeFalse();
    }

    [Fact]
    public async Task Action_WhenTheHistoryStoreFails_WithFailOnErrorLogs_ShouldFail()
    {
        // Arrange
        var store = new GatedStore();
        await using var bot = await TestBot.StartAsync(services: s =>
            s.AddTelegramHistory().Services.AddSingleton<ITelegramHistoryStore>(store)
        );
        var nick = bot.PrivateChat("Nick");
        await bot.History.FlushAsync();
        store.Failure = new InvalidOperationException("The database is down.");
        bot.FailOnErrorLogs = true;

        // Act
        var act = () => nick.SendsAsync("hi");

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("The bot logged an error outside any update*The Telegram history store failed*");
    }

    [Fact]
    public async Task History_OfAnAppThatRegistersItsOwnClientFirst_ShouldRecordItsCalls()
    {
        // Arrange
        await using var bot = await TestBot.StartAsync(services: s =>
        {
            s.AddSingleton<ITelegramBotClient>(new TelegramBotClient(OwnToken));
            s.AddTelegramHistory().UseInMemory();
        });
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("hi");

        // Assert
        (await bot.History.ReadAsync(new() { ChatId = nick.Chat.Id }))
            .Select(x => x.Kind)
            .Should()
            .Equal("message", "sendMessage");
    }

    [Fact]
    public async Task Start_WhenTheAppRegistersItsClientAfterAddTelegramHistory_ShouldFailAsInProduction()
    {
        // Act
        var act = () =>
            TestBot.StartAsync(services: s =>
            {
                s.AddTelegramHistory().UseInMemory();
                s.AddSingleton<ITelegramBotClient>(new TelegramBotClient(OwnToken));
            });

        // Assert
        await act.Should()
            .ThrowAsync<OptionsValidationException>()
            .WithMessage("*Move AddTelegramHistory() below your own ITelegramBotClient registration*");
    }

    private static void WithHistory(IServiceCollection services) => services.AddTelegramHistory().UseInMemory();

    // A store that, once closed, holds each batch until opened, and signals when one arrives; it can fail instead.
    private sealed class GatedStore : ITelegramHistoryStore
    {
        private TaskCompletionSource _gate = Opened();
        private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Failure { get; set; }

        // Completes when a batch arrives after Close.
        public Task Entered => _entered.Task;

        public void Close()
        {
            _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Open() => _gate.TrySetResult();

        public async Task AppendAsync(IReadOnlyList<TelegramHistoryEntry> entries, CancellationToken token)
        {
            _entered.TrySetResult();
            await _gate.Task.WaitAsync(token);
            if (Failure is { } failure)
            {
                throw failure;
            }
        }

        public Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
            TelegramHistoryQuery query,
            CancellationToken token
        ) => throw new NotSupportedException();

        private static TaskCompletionSource Opened()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate.SetResult();
            return gate;
        }
    }
}
