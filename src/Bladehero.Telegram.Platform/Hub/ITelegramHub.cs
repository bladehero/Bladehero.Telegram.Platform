namespace Bladehero.Telegram.Platform.Hub;

/// <summary>What an app gets from the hub in front of Telegram, beyond the Bot API: its people, and its sign-in links.</summary>
/// <remarks>Registered by <c>AddTelegramHub</c>.</remarks>
public interface ITelegramHub
{
    /// <summary>
    /// Whether the hub's list of people has arrived since the app started. Until it has, <see cref="People"/> is empty
    /// because nobody knows yet, not because nobody is allowed: keep signed-in people signed in, and refuse new sign-ins.
    /// </summary>
    bool KnowsPeople { get; }

    /// <summary>The latest copy of the people the hub lists for this app. It stays while the hub is down.</summary>
    IReadOnlyList<TelegramHubPerson> People { get; }

    /// <summary>The person with this Telegram id on the latest copy of the list, or <c>null</c>.</summary>
    TelegramHubPerson? FindPerson(long telegramId);

    /// <summary>
    /// A link that opens the bot in Telegram with <paramref name="code"/> as its start parameter. The hub takes the
    /// app's prefix off and passes this app a plain <c>/start &lt;code&gt;</c>.
    /// </summary>
    /// <remarks>With a real token rather than a hub's key, there's no prefix to add.</remarks>
    ValueTask<string> SignInLinkAsync(string code, CancellationToken cancellationToken = default);
}
