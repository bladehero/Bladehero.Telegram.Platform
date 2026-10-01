using System.Collections.Concurrent;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Coffee;

// The bot stops and starts again on the same fake, which keeps the chats, as Telegram would.
public sealed class RestartTests
{
    [Fact]
    public async Task Coffee_AfterARestartWithASharedStore_ShouldCarryOn()
    {
        // Arrange
        var api = new FakeBotApi();
        var store = new SharedConversationStore();
        await using (var before = await StartAsync(api, store))
        {
            await PickMediumAsync(before.PrivateChat("Nick"));
        }

        await using var bot = await StartAsync(api, store);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("Nicky");
        await nick.TapsAsync("Confirm");

        // Assert
        nick.LastMessage.Text.Should().Be("Order placed ☕ — a Medium coffee for Nicky.");
    }

    [Fact]
    public async Task Coffee_AfterARestartWithoutAStore_ShouldForgetTheOrder()
    {
        // Arrange
        var api = new FakeBotApi();
        await using (var before = await StartAsync(api))
        {
            await PickMediumAsync(before.PrivateChat("Nick"));
        }

        await using var bot = await StartAsync(api);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("Nicky");

        // Assert
        nick.LastMessage.Text.Should().Be("Send /coffee to order one ☕");
    }

    [Fact]
    public async Task SizeButton_AfterARestartWithoutAStore_ShouldSayItIsNoLongerActive()
    {
        // Arrange
        var api = new FakeBotApi();
        await using (var before = await StartAsync(api))
        {
            await before.PrivateChat("Nick").SendsAsync("/coffee");
        }

        await using var bot = await StartAsync(api);
        var nick = bot.PrivateChat("Nick");

        // Act
        var answer = await nick.TapsAsync("Medium");

        // Assert
        answer.ToString().Should().Be("Notification: That button is no longer active.");
    }

    [Fact]
    public async Task ConfirmButton_AfterARestartWithAStoreThatDropsTheId_ShouldSayItIsNoLongerActive()
    {
        // Arrange: the order reaches its confirmation by text alone, as the store loses every run id.
        var api = new FakeBotApi();
        var store = new SharedConversationStore(keepsTheId: false);
        await using (var before = await StartAsync(api, store))
        {
            var customer = before.PrivateChat("Nick");
            await customer.SendsAsync("/coffee medium");
            await customer.SendsAsync("Nicky");
        }

        await using var bot = await StartAsync(api, store);
        var nick = bot.PrivateChat("Nick");

        // Act
        var answer = await nick.TapsAsync("Confirm");

        // Assert
        using (new AssertionScope())
        {
            answer.ToString().Should().Be("Notification: That button is no longer active.");
            nick.LastMessage.ToString()
                .Should()
                .Be("Bot: A Medium coffee for Nicky. Place the order? [Confirm] [Cancel]");
        }
    }

    [Fact]
    public async Task Members_AfterARestart_ShouldBeSeededAgain()
    {
        // Arrange
        var api = new FakeBotApi();
        var members = SandboxBot.Members((api.UserIdOf("Nick"), "Nick", 40));
        await using (var before = await SandboxBot.StartAsync(api, members))
        {
            await before.PrivateChat("Nick").SendsAsync("/redeem 10");
        }

        await using var bot = await SandboxBot.StartAsync(api, members);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/points");

        // Assert
        nick.LastMessage.Text.Should().Be("Nick, you have 40 points.");
    }

    private static Task<TelegramTestHost> StartAsync(FakeBotApi api, IConversationStore? store = null) =>
        SandboxBot.StartAsync(api, configure: store is null ? null : builder => builder.Services.AddSingleton(store));

    private static async Task PickMediumAsync(TestUser customer)
    {
        await customer.SendsAsync("/coffee");
        await customer.TapsAsync("Medium");
    }

    // Outlives the bot, as a database would; one that keeps only the flow, the step and the data loses the run id.
    private sealed class SharedConversationStore(bool keepsTheId = true) : IConversationStore
    {
        private readonly ConcurrentDictionary<ConversationKey, ConversationState> _conversations = new();

        public Task<ConversationState?> GetAsync(ConversationKey key, CancellationToken token) =>
            Task.FromResult(_conversations.GetValueOrDefault(key));

        public Task SaveAsync(ConversationKey key, ConversationState state, CancellationToken token)
        {
            _conversations[key] = keepsTheId ? state : new ConversationState(state.Flow, state.Step, state.Data);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(ConversationKey key, CancellationToken token)
        {
            _conversations.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
