using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.History.InMemory.Tests;

public sealed class InMemoryTelegramHistoryStoreTests
{
    private const long Group = -1001234567890;
    private const long OtherGroup = -1009876543210;
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendAsync_ShouldGiveIncreasingIds()
    {
        // Arrange
        var sut = new InMemoryTelegramHistoryStore(maxEntriesPerChat: 10);

        // Act
        await sut.AppendAsync([Entry("#1"), Entry("#2")], CancellationToken.None);
        await sut.AppendAsync([Entry("#3", chatId: OtherGroup)], CancellationToken.None);

        // Assert
        (await ReadAsync(sut, new()))
            .Select(x => (x.Id, x.Text))
            .Should()
            .Equal((1, "#1"), (2, "#2"), (3, "#3"));
    }

    [Fact]
    public async Task AppendAsync_OverTheCapOfAChat_ShouldDropItsOldestOnly()
    {
        // Arrange
        var sut = new InMemoryTelegramHistoryStore(maxEntriesPerChat: 2);

        // Act
        await sut.AppendAsync(
            [
                Entry("#1"),
                Entry("#2"),
                Entry("other #1", chatId: OtherGroup),
                Entry("#3"),
                Entry("other #2", OtherGroup),
            ],
            CancellationToken.None
        );

        // Assert
        using (new AssertionScope())
        {
            Texts(await ReadAsync(sut, new() { ChatId = Group })).Should().Equal("#2", "#3");
            Texts(await ReadAsync(sut, new() { ChatId = OtherGroup })).Should().Equal("other #1", "other #2");
        }
    }

    [Fact]
    public async Task AppendAsync_WithoutAChat_ShouldCapThoseEntriesToo()
    {
        // Arrange
        var sut = new InMemoryTelegramHistoryStore(maxEntriesPerChat: 2);

        // Act
        await sut.AppendAsync(
            [Entry("query #1", chatId: null), Entry("query #2", chatId: null), Entry("query #3", chatId: null)],
            CancellationToken.None
        );

        // Assert
        Texts(await ReadAsync(sut, new())).Should().Equal("query #2", "query #3");
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnTheLatestOldestFirst()
    {
        // Arrange
        var sut = await StoreWithAsync(Entry("#1"), Entry("#2"), Entry("#3"), Entry("#4"), Entry("#5"));

        // Act
        var entries = await ReadAsync(sut, new() { ChatId = Group, Limit = 3 });

        // Assert
        Texts(entries).Should().Equal("#3", "#4", "#5");
    }

    [Fact]
    public async Task ReadAsync_BeforeAnId_ShouldReturnThePageBeforeIt()
    {
        // Arrange
        var sut = await StoreWithAsync(Entry("#1"), Entry("#2"), Entry("#3"), Entry("#4"), Entry("#5"));
        var query = new TelegramHistoryQuery { ChatId = Group, Limit = 2 };
        var latest = await ReadAsync(sut, query);

        // Act
        var before = await ReadAsync(sut, query with { BeforeId = latest[0].Id });

        // Assert
        using (new AssertionScope())
        {
            Texts(latest).Should().Equal("#4", "#5");
            Texts(before).Should().Equal("#2", "#3");
        }
    }

    [Fact]
    public async Task ReadAsync_Since_ShouldIncludeEntriesFromThatTimeOn()
    {
        // Arrange
        var sut = await StoreWithAsync(
            Entry("#1", time: Now.AddMinutes(-2)),
            Entry("#2", time: Now.AddMinutes(-1)),
            Entry("#3", time: Now)
        );

        // Act: the same instant as #2, in another offset.
        var entries = await ReadAsync(sut, new() { Since = Now.AddMinutes(-1).ToOffset(TimeSpan.FromHours(3)) });

        // Assert
        Texts(entries).Should().Equal("#2", "#3");
    }

    [Fact]
    public async Task ReadAsync_ByMessage_ShouldReturnItsLifecycle()
    {
        // Arrange
        var sut = await StoreWithAsync(
            Entry("Latte?", kind: "sendMessage", messageId: 10),
            Entry("Large latte?", kind: "editMessageText", messageId: 10),
            Entry("Hi", kind: "sendMessage", messageId: 11),
            Entry("size:large", kind: "callback_query", messageId: 10),
            Entry("Elsewhere", kind: "sendMessage", chatId: OtherGroup, messageId: 10),
            Entry(null, kind: "deleteMessage", messageId: 10)
        );

        // Act
        var entries = await ReadAsync(sut, new() { ChatId = Group, MessageId = 10 });

        // Assert
        entries
            .Select(x => x.Kind)
            .Should()
            .Equal("sendMessage", "editMessageText", "callback_query", "deleteMessage");
    }

    [Fact]
    public async Task ReadAsync_ByInlineMessage_ShouldReturnItsEntries()
    {
        // Arrange
        var sut = await StoreWithAsync(
            Entry("latte", kind: "chosen_inline_result", chatId: null, inlineMessageId: "AAAAinline"),
            Entry("mocha", kind: "chosen_inline_result", chatId: null, inlineMessageId: "AAAAother"),
            Entry("Done", kind: "editMessageText", chatId: null, inlineMessageId: "AAAAinline")
        );

        // Act
        var entries = await ReadAsync(sut, new() { InlineMessageId = "AAAAinline" });

        // Assert
        Texts(entries).Should().Equal("latte", "Done");
    }

    [Fact]
    public async Task ReadAsync_ByUpdate_ShouldReturnItAndWhatItCausedAcrossChats()
    {
        // Arrange
        var sut = await StoreWithAsync(
            Entry("size:large", kind: "callback_query", updateId: 7),
            Entry("/start", kind: "message", updateId: 8),
            Entry("Saved", kind: "answerCallbackQuery", updateId: 7),
            Entry("New order", kind: "sendMessage", chatId: OtherGroup, updateId: 7),
            Entry("Done", kind: "editMessageText", chatId: null, updateId: 7)
        );

        // Act
        var entries = await ReadAsync(sut, new() { UpdateId = 7 });

        // Assert
        entries
            .Select(x => x.Kind)
            .Should()
            .Equal("callback_query", "answerCallbackQuery", "sendMessage", "editMessageText");
    }

    [Fact]
    public async Task ReadAsync_WithoutFilters_ShouldReturnTheLatestOfAllChats()
    {
        // Arrange
        var sut = await StoreWithAsync(
            Entry("#1"),
            Entry("other #1", chatId: OtherGroup),
            Entry("query #1", chatId: null),
            Entry("#2"),
            Entry("other #2", chatId: OtherGroup)
        );

        // Act
        var entries = await ReadAsync(sut, new() { Limit = 3 });

        // Assert
        Texts(entries).Should().Equal("query #1", "#2", "other #2");
    }

    private static TelegramHistoryEntry Entry(
        string? text,
        long? chatId = Group,
        string kind = "message",
        int? messageId = null,
        string? inlineMessageId = null,
        int? updateId = null,
        DateTimeOffset? time = null
    ) =>
        new()
        {
            Kind = kind,
            Text = text,
            ChatId = chatId,
            MessageId = messageId,
            InlineMessageId = inlineMessageId,
            UpdateId = updateId,
            Time = time ?? Now,
        };

    private static async Task<InMemoryTelegramHistoryStore> StoreWithAsync(params TelegramHistoryEntry[] entries)
    {
        var store = new InMemoryTelegramHistoryStore(maxEntriesPerChat: 100);
        await store.AppendAsync(entries, CancellationToken.None);
        return store;
    }

    private static Task<IReadOnlyList<TelegramHistoryEntry>> ReadAsync(
        InMemoryTelegramHistoryStore store,
        TelegramHistoryQuery query
    ) => store.ReadAsync(query, CancellationToken.None);

    private static IEnumerable<string?> Texts(IEnumerable<TelegramHistoryEntry> entries) => entries.Select(x => x.Text);
}
