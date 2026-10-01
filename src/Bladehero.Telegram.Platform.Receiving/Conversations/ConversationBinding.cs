using System.Globalization;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>What ties a button to a conversation: its user and the run it was shown for.</summary>
/// <remarks>Get one from <c>BindAsync</c>, or build one for a state saved outside an update.</remarks>
/// <param name="UserId">The Telegram user the button is shown for.</param>
/// <param name="ConversationId">The run, <see cref="ConversationState.Id"/>: 1-16 characters of a-z and 0-9.</param>
public readonly record struct ConversationBinding(long UserId, string ConversationId)
{
    private const int LongestId = 16;

    // "@7000000001.k3j9x2ab"; the conversation id is checked here, as a record can't check its own parameters.
    internal string Suffix()
    {
        if (!IsConversationId(ConversationId))
        {
            throw new ArgumentException(
                $"A conversation id is 1-{LongestId} characters of a-z and 0-9, not \"{ConversationId}\".",
                "binding"
            );
        }

        return string.Create(CultureInfo.InvariantCulture, $"@{UserId:D}.{ConversationId}");
    }

    // A suffix is well-formed only in the one form Suffix writes.
    internal static bool TryParse(string suffix, out ConversationBinding binding)
    {
        binding = default;

        var dot = suffix.IndexOf('.');
        if (
            dot < 0
            || !long.TryParse(
                suffix.AsSpan(0, dot),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var userId
            )
            || userId.ToString("D", CultureInfo.InvariantCulture) != suffix[..dot]
            || !IsConversationId(suffix[(dot + 1)..])
        )
        {
            return false;
        }

        binding = new ConversationBinding(userId, suffix[(dot + 1)..]);
        return true;
    }

    private static bool IsConversationId(string? id) =>
        id is { Length: > 0 and <= LongestId }
        && id.All(letter => letter is (>= 'a' and <= 'z') or (>= '0' and <= '9'));
}
