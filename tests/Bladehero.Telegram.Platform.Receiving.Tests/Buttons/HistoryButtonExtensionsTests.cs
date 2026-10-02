using System.Text.Json;
using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.History;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Buttons;

public sealed class HistoryButtonExtensionsTests
{
    private const long Nick = 7000000001;
    private const long Anna = 7000000002;
    private const long Chat = Nick;

    private readonly FakeHistory _history = new();

    [Fact]
    public async Task FindLatestWithButtonAsync_ShouldReturnTheNewestCardShowingTheButton()
    {
        // Arrange
        _history.Add(
            Call("sendMessage", 1, Card(1, Redeem(Nick))),
            Call("sendMessage", 2, Card(2, Redeem(Nick))),
            Call("sendMessage", 3, Card(3))
        );

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        card.Should().Be(new TelegramMessageRef(Chat, 2));
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_WithAMatch_ShouldSkipCardsForSomeoneElse()
    {
        // Arrange
        _history.Add(Call("sendMessage", 1, Card(1, Redeem(Nick))), Call("sendMessage", 2, Card(2, Redeem(Anna))));

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat, x => x.OwnerId == Nick);

        // Assert
        card.Should().Be(new TelegramMessageRef(Chat, 1));
    }

    [Theory]
    [InlineData("deleteMessage")]
    [InlineData("deleteMessages")]
    public async Task FindLatestWithButtonAsync_OfADeletedCard_ShouldReturnNull(string kind)
    {
        // Arrange
        _history.Add(Call("sendMessage", 1, Card(1, Redeem(Nick))), Call(kind, 1, true));

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        card.Should().BeNull();
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_AfterTheKeyboardWasCleared_ShouldReturnNull()
    {
        // Arrange
        _history.Add(Call("sendMessage", 1, Card(1, Redeem(Nick))), Call("editMessageReplyMarkup", 1, Card(1)));

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        card.Should().BeNull();
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_AfterAFailedEdit_ShouldStillFindTheCard()
    {
        // Arrange
        _history.Add(
            Call("sendMessage", 1, Card(1, Redeem(Nick))),
            Call("editMessageText", 1, result: null) with
            {
                ErrorCode = 429,
                Error = "Too Many Requests: retry after 5",
            }
        );

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        card.Should().Be(new TelegramMessageRef(Chat, 1));
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_AfterAReaction_ShouldStillFindTheCard()
    {
        // Arrange
        _history.Add(Call("sendMessage", 1, Card(1, Redeem(Nick))), Call("setMessageReaction", 1, true));

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        card.Should().Be(new TelegramMessageRef(Chat, 1));
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_WithABoundButton_ShouldFindIt()
    {
        // Arrange
        var bound = ButtonData.Button(
            "Redeem 10",
            new RedeemButton(Nick, 10),
            new ConversationBinding(Nick, "abc12345")
        );
        _history.Add(Call("sendMessage", 1, Card(1, bound)));

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        card.Should().Be(new TelegramMessageRef(Chat, 1));
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_BeyondFiveHundredEntries_ShouldReturnNull()
    {
        // Arrange
        _history.Add(Call("sendMessage", 1, Card(1, Redeem(Nick))));
        _history.Add([.. Enumerable.Range(2, 500).Select(id => Call("sendMessage", id, Card(id)))]);

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        using (new AssertionScope())
        {
            card.Should().BeNull();
            _history.Queries.Should().Equal(new TelegramHistoryQuery { ChatId = Chat, Limit = 500 });
        }
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_WithoutJson_ShouldThrow()
    {
        // Arrange
        _history.Add(Call("sendMessage", 1, Card(1, Redeem(Nick))) with { Json = null });

        // Act
        var act = () => _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "Finding a message by its buttons needs the history's JSON: KeepJson is off, or a Filter removed it."
            );
    }

    [Fact]
    public async Task FindLatestWithButtonAsync_InAChatWithoutCards_ShouldReturnNull()
    {
        // Arrange: Nick's message, a reply, and a card with other buttons.
        _history.Add(
            new TelegramHistoryEntry
            {
                Direction = TelegramHistoryDirection.Incoming,
                Kind = "message",
                ChatId = Chat,
                MessageId = 1,
                Json = "{}",
            },
            Call("sendMessage", 2, Card(2)),
            Call("sendMessage", 3, Card(3, ButtonData.Button("Skip", new SkipButton(Nick))))
        );

        // Act
        var card = await _history.FindLatestWithButtonAsync<RedeemButton>(Chat);

        // Assert
        card.Should().BeNull();
    }

    private static InlineKeyboardButton Redeem(long ownerId) =>
        ButtonData.Button("Redeem 10", new RedeemButton(ownerId, 10));

    // The message as Telegram returned it, with its keyboard as it then stood.
    private static Message Card(int messageId, params InlineKeyboardButton[] buttons) =>
        new()
        {
            Id = messageId,
            Date = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc),
            Chat = new Chat { Id = Chat, Type = ChatType.Private },
            Text = "Nick, you have 50 points.",
            ReplyMarkup = buttons is [] ? null : new InlineKeyboardMarkup(buttons),
        };

    // A successful call about a message, shaped like the recorder's entry: the request and the result.
    private static TelegramHistoryEntry Call(string kind, int messageId, object? result)
    {
        var json = new JsonObject
        {
            ["request"] = new JsonObject { ["chat_id"] = Chat, ["message_id"] = messageId },
        };
        if (result is not null)
        {
            json["result"] = JsonSerializer.SerializeToNode(result, result.GetType(), JsonBotAPI.Options);
        }

        return new TelegramHistoryEntry
        {
            Direction = TelegramHistoryDirection.Outgoing,
            Kind = kind,
            ChatId = Chat,
            MessageId = messageId,
            Json = json.ToJsonString(),
        };
    }

    [ButtonData("find-redeem")]
    private readonly record struct RedeemButton(long OwnerId, int Points);

    [ButtonData("find-skip")]
    private readonly record struct SkipButton(long OwnerId);

    // Answers as a store does: the newest Limit entries before BeforeId, oldest first.
    private sealed class FakeHistory : ITelegramHistory
    {
        private readonly List<TelegramHistoryEntry> _entries = [];
        private readonly List<TelegramHistoryQuery> _queries = [];

        public IReadOnlyList<TelegramHistoryQuery> Queries => _queries;

        public void Add(params TelegramHistoryEntry[] entries) =>
            _entries.AddRange(entries.Select((x, i) => x with { Id = _entries.Count + i + 1 }));

        public Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
            TelegramHistoryQuery query,
            CancellationToken token = default
        )
        {
            _queries.Add(query);
            return Task.FromResult<IReadOnlyList<TelegramHistoryEntry>>([
                .. _entries
                    .Where(x => x.ChatId == query.ChatId && (query.BeforeId is null || x.Id < query.BeforeId))
                    .TakeLast(query.Limit),
            ]);
        }

        public Task FlushAsync(CancellationToken token = default) => Task.CompletedTask;
    }
}
