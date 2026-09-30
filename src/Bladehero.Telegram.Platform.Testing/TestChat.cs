using System.Diagnostics;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>A private or group chat with the bot, as its members see it.</summary>
public sealed class TestChat
{
    private readonly TelegramTestHost _host;
    private readonly string _name;
    private readonly bool _isGroup;

    internal TestChat(TelegramTestHost host, long id, string name, bool isGroup)
    {
        _host = host;
        _name = name;
        _isGroup = isGroup;
        Id = id;
    }

    /// <summary>The Telegram chat id: the user's id for a private chat, negative for a group.</summary>
    public long Id { get; }

    /// <summary>All messages, oldest first, with edits applied and deleted ones gone.</summary>
    public IReadOnlyList<TestMessage> Messages =>
        [.. _host.Api.MessagesIn(Id).Select(json => new TestMessage(json, _host.Api))];

    /// <summary>The newest message, whoever sent it.</summary>
    /// <exception cref="InvalidOperationException">The chat has no messages.</exception>
    public TestMessage LastMessage =>
        Messages.LastOrDefault() ?? throw new InvalidOperationException($"There are no messages in {Description}.");

    /// <summary>The newest message the bot sent here; an edit doesn't move it.</summary>
    /// <exception cref="InvalidOperationException">The bot has sent nothing here.</exception>
    public TestMessage LastReply =>
        Messages.LastOrDefault(message => message.IsFromBot)
        ?? throw new InvalidOperationException($"The bot has sent nothing in {Description}.");

    internal string Description => _isGroup ? $"the {_name} group" : $"the chat with {_name}";

    /// <summary><paramref name="message"/> as it now stands, or <c>null</c> once deleted.</summary>
    /// <exception cref="ArgumentException"><paramref name="message"/> is from another chat.</exception>
    public TestMessage? Current(TestMessage message)
    {
        ThrowIfFromAnotherChat(message);

        return _host.Api.MessageIn(Id, message.Id) is { } json ? new TestMessage(json, _host.Api) : null;
    }

    /// <summary>Every state <paramref name="message"/> has had, oldest first.</summary>
    /// <exception cref="ArgumentException"><paramref name="message"/> is from another chat.</exception>
    public IReadOnlyList<TestMessage> RevisionsOf(TestMessage message)
    {
        ThrowIfFromAnotherChat(message);

        return [.. _host.Api.RevisionsIn(Id, message.Id).Select(json => new TestMessage(json, _host.Api))];
    }

    /// <summary>
    /// Waits for a message <paramref name="match"/> accepts, such as one a background job sends after the update
    /// that started it was handled.
    /// </summary>
    /// <param name="match">Accepts the message waited for.</param>
    /// <param name="after">
    /// Only messages newer than this one of the chat count, typically the trigger:
    /// <c>after: await nick.SendsAsync("/import")</c>.
    /// </param>
    /// <param name="timeout">
    /// How long to wait; the host's <see cref="TelegramTestHost.UpdateTimeout"/> by default.
    /// </param>
    /// <param name="token">Stops waiting.</param>
    /// <returns>
    /// The newest matching message as the chat now stands; failing that, the first message to match later, whether
    /// new or edited.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="after"/> is from another chat.</exception>
    /// <exception cref="TimeoutException">No message matched in time; the error shows the chat.</exception>
    /// <remarks>
    /// Without <paramref name="after"/>, a matching message already in the chat, such as the notice of an earlier
    /// run, is returned at once. It looks again on every change to the chat: a message posted, edited or deleted.
    /// </remarks>
    public async Task<TestMessage> WaitForMessageAsync(
        Func<TestMessage, bool> match,
        TestMessage? after = null,
        TimeSpan? timeout = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(match);

        if (after is not null && after.Message.Chat.Id != Id)
        {
            throw new ArgumentException(
                $"The message \"{after}\" is from another chat, not {Description}; wait after one of its messages.",
                nameof(after)
            );
        }

        Func<TestMessage, bool> counts = after is null ? match : message => message.Id > after.Id && match(message);
        var limit = timeout ?? _host.UpdateTimeout;
        if (limit <= TimeSpan.Zero && limit != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), limit, "Waiting needs some time.");
        }

        var clock = Stopwatch.StartNew();
        for (var firstLook = true; ; firstLook = false)
        {
            var (now, nextChange) = _host.Api.MessagesAndNextChange(Id);
            TestMessage[] messages = [.. now.Select(json => new TestMessage(json, _host.Api))];

            // Nothing matched before a later look, so whatever matches then is new.
            if ((firstLook ? messages.LastOrDefault(counts) : messages.FirstOrDefault(counts)) is { } found)
            {
                return found;
            }

            try
            {
                await nextChange.WaitAsync(TelegramTestHost.Left(limit, clock), token);
            }
            catch (TimeoutException)
            {
                var shown = Messages.Select(message => message.ToString()).ToArray();
                throw new TimeoutException(
                    $"No message in {Description} matched within {TelegramTestHost.Describe(limit)}. "
                        + (
                            shown.Length == 0
                                ? "The chat is empty."
                                : $"The chat now shows:\n{string.Join("\n", shown)}"
                        )
                );
            }
        }
    }

    /// <summary><paramref name="firstName"/> as a member of this group.</summary>
    /// <param name="firstName">The user's first name, which is one user throughout the test.</param>
    /// <param name="lastName">The last name, if any.</param>
    /// <param name="username">The username without @, e.g. <c>nick_d</c>, if any.</param>
    /// <param name="languageCode">The app's language, e.g. <c>en</c> or <c>pt-br</c>, if any.</param>
    /// <exception cref="ArgumentException">A detail isn't one Telegram gives.</exception>
    /// <exception cref="InvalidOperationException">
    /// This is a private chat, a detail differs from the one the user was first opened with, or another user has the
    /// username.
    /// </exception>
    public TestUser Member(
        string firstName,
        string? lastName = null,
        string? username = null,
        string? languageCode = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);

        if (!_isGroup)
        {
            throw new InvalidOperationException(
                $"The chat with {_name} is private, and only a group has members. Start one with GroupChat."
            );
        }

        return new TestUser(_host, _host.Api.Person(firstName, lastName, username, languageCode), this);
    }

    /// <summary>The group's title, or the name of the user in a private chat.</summary>
    public override string ToString() => _name;

    private void ThrowIfFromAnotherChat(TestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Message.Chat.Id != Id)
        {
            throw new ArgumentException(
                $"The message \"{message.Content}\" is from another chat, not {Description}.",
                nameof(message)
            );
        }
    }
}
