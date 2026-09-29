using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Welcome;

// Telegram tells the bot about its own membership in a raw update, which also makes the group known to the fake.
public sealed class GroupGreetingTests
{
    private const long GroupId = -1009876543210;

    // The fake's bot.
    private static User Bot =>
        new()
        {
            Id = 1234567,
            IsBot = true,
            FirstName = "Test Bot",
            Username = "test_bot",
        };

    [Theory]
    [InlineData(ChatMemberStatus.Member)]
    [InlineData(ChatMemberStatus.Administrator)]
    public async Task AddedToAGroup_ShouldGreetIt(ChatMemberStatus status)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();

        // Act
        await bot.SendAsync(BotChanges(bot.Api, from: ChatMemberStatus.Left, to: status));

        // Assert
        var greeting = bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendMessage").Subject.Parameters;
        using (new AssertionScope())
        {
            greeting["chat_id"]!.GetValue<long>().Should().Be(GroupId);
            greeting["text"]!.GetValue<string>().Should().Be("Hi Coffee Lovers! Send /coffee to order.");
        }
    }

    [Theory]
    [InlineData(ChatMemberStatus.Left)]
    [InlineData(ChatMemberStatus.Kicked)]
    public async Task RemovedFromAGroup_ShouldSayNothing(ChatMemberStatus status)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var calls = bot.Api.Calls.Count;

        // Act
        await bot.SendAsync(BotChanges(bot.Api, from: ChatMemberStatus.Member, to: status));

        // Assert
        bot.Api.Calls.Should().HaveCount(calls);
    }

    // Nick changes the bot's status in the Coffee Lovers group.
    private static Update BotChanges(FakeBotApi api, ChatMemberStatus from, ChatMemberStatus to) =>
        new()
        {
            MyChatMember = new ChatMemberUpdated
            {
                Chat = new Chat
                {
                    Id = GroupId,
                    Type = ChatType.Supergroup,
                    Title = "Coffee Lovers",
                },
                From = new User { Id = api.UserIdOf("Nick"), FirstName = "Nick" },
                Date = DateTime.UtcNow,
                OldChatMember = Status(from),
                NewChatMember = Status(to),
            },
        };

    private static ChatMember Status(ChatMemberStatus status) =>
        status switch
        {
            ChatMemberStatus.Left => new ChatMemberLeft { User = Bot },
            ChatMemberStatus.Kicked => new ChatMemberBanned { User = Bot },
            ChatMemberStatus.Member => new ChatMemberMember { User = Bot },
            ChatMemberStatus.Administrator => new ChatMemberAdministrator { User = Bot },
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
}
