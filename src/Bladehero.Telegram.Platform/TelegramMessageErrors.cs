using Telegram.Bot.Exceptions;

namespace Bladehero.Telegram.Platform;

// Telegram's texts, as the Bot API server sends them.
internal static class TelegramMessageErrors
{
    public static bool IsNotModified(ApiRequestException error) => Says(error, "message is not modified");

    public static bool IsGoneForEdit(ApiRequestException error) =>
        Says(error, "message to edit not found") || Says(error, "MESSAGE_ID_INVALID");

    public static bool IsUneditable(ApiRequestException error) =>
        Says(error, "message can't be edited") || Says(error, "there is no text in the message to edit");

    public static bool IsGoneForDelete(ApiRequestException error) =>
        Says(error, "message to delete not found") || Says(error, "MESSAGE_ID_INVALID");

    // Also "… for everyone".
    public static bool IsUndeletable(ApiRequestException error) => Says(error, "message can't be deleted");

    private static bool Says(ApiRequestException error, string text) =>
        error.Message.Contains(text, StringComparison.OrdinalIgnoreCase);
}
