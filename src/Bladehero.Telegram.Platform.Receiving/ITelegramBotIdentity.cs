using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving;

/// <summary>The bot itself, as getMe describes it: its id and username.</summary>
public interface ITelegramBotIdentity
{
    /// <summary>The bot, once a getMe has succeeded; <c>null</c> until then.</summary>
    User? Current { get; }

    /// <summary>
    /// The bot, from one getMe that concurrent callers share, cached once it succeeds; a failure isn't cached.
    /// </summary>
    ValueTask<User> GetAsync(CancellationToken token);
}
