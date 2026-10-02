using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.History;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Finds the bot's messages in its history by their buttons.</summary>
public static class HistoryButtonExtensions
{
    private const int Entries = 500;

    /// <summary>
    /// The newest message the bot sent to the chat that still shows a <typeparamref name="TButton"/> button (matching
    /// <paramref name="match"/>), found in the chat's latest 500 history entries; null when there is none.
    /// </summary>
    /// <typeparam name="TButton">A <c>[ButtonData]</c> struct.</typeparam>
    /// <param name="history">The bot's history.</param>
    /// <param name="chatId">The chat to look in.</param>
    /// <param name="match">Which buttons count, e.g. only one user's; any when <c>null</c>.</param>
    /// <param name="token">Cancels the read.</param>
    /// <exception cref="InvalidOperationException">
    /// The history has no JSON (<c>KeepJson</c> is off, or a <c>Filter</c> removed it), or
    /// <typeparamref name="TButton"/> can't be button data.
    /// </exception>
    public static async Task<TelegramMessageRef?> FindLatestWithButtonAsync<TButton>(
        this ITelegramHistory history,
        long chatId,
        Func<TButton, bool>? match = null,
        CancellationToken token = default
    )
        where TButton : struct
    {
        ArgumentNullException.ThrowIfNull(history);

        // Fails at once for a type that can't be button data.
        ButtonData.TryDecode<TButton>(null, out _);

        var entries = await history.ReadAsync(new TelegramHistoryQuery { ChatId = chatId, Limit = Entries }, token);

        // The newest successful call about a message decides it.
        var decided = new HashSet<int>();
        foreach (var entry in entries.Reverse())
        {
            if (
                entry is not { Direction: TelegramHistoryDirection.Outgoing, Error: null, MessageId: { } messageId }
                || decided.Contains(messageId)
            )
            {
                continue;
            }

            if (entry.Kind is "deleteMessage" or "deleteMessages")
            {
                decided.Add(messageId);
                continue;
            }

            if (entry.Json is null)
            {
                throw new InvalidOperationException(
                    "Finding a message by its buttons needs the history's JSON: "
                        + "KeepJson is off, or a Filter removed it."
                );
            }

            // A call that returns no message, such as a reaction, leaves the keyboard as it was.
            if (JsonNode.Parse(entry.Json)?["result"] is not JsonObject result || !result.ContainsKey("chat"))
            {
                continue;
            }

            decided.Add(messageId);
            if (Shows(result, match))
            {
                return new TelegramMessageRef(chatId, messageId);
            }
        }

        return null;
    }

    // The message's keyboard as it stands after the call; none once cleared.
    private static bool Shows<TButton>(JsonObject message, Func<TButton, bool>? match)
        where TButton : struct =>
        message["reply_markup"]?["inline_keyboard"] is JsonArray rows
        && rows.OfType<JsonArray>()
            .SelectMany(row => row)
            .Any(button =>
                button?["callback_data"] is JsonValue data
                && data.TryGetValue(out string? text)
                && ButtonData.TryDecode(text, out TButton decoded)
                && (match?.Invoke(decoded) ?? true)
            );
}
