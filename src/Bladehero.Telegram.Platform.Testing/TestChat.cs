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

    internal string Description => _isGroup ? $"the {_name} group" : $"the chat with {_name}";

    /// <summary>
    /// Waits for a message <paramref name="match"/> accepts, such as one a background job sends after the update
    /// that started it was handled.
    /// </summary>
    /// <param name="match">Accepts the message waited for.</param>
    /// <param name="timeout">
    /// How long to wait; the host's <see cref="TelegramTestHost.UpdateTimeout"/> by default.
    /// </param>
    /// <param name="token">Stops waiting.</param>
    /// <returns>
    /// The newest matching message as the chat now stands; failing that, the first message to match later, whether
    /// new or edited.
    /// </returns>
    /// <exception cref="TimeoutException">No message matched in time; the error shows the chat.</exception>
    /// <remarks>It looks again on every change to the chat: a message posted, edited or deleted.</remarks>
    public async Task<TestMessage> WaitForMessageAsync(
        Func<TestMessage, bool> match,
        TimeSpan? timeout = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(match);

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
            if ((firstLook ? messages.LastOrDefault(match) : messages.FirstOrDefault(match)) is { } found)
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
    /// <exception cref="InvalidOperationException">This is a private chat.</exception>
    public TestUser Member(string firstName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);

        if (!_isGroup)
        {
            throw new InvalidOperationException(
                $"The chat with {_name} is private, and only a group has members. Start one with GroupChat."
            );
        }

        return new TestUser(_host, _host.Api.Person(firstName), this);
    }

    /// <summary>The group's title, or the name of the user in a private chat.</summary>
    public override string ToString() => _name;
}
