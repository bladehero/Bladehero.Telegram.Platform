using System.Text.Json.Serialization;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

internal static class CoffeeFlow
{
    public const string Name = "coffee";

    public const string SizeStep = "size";
    public const string NameStep = "name";
    public const string ConfirmStep = "confirm";

    public const string Buttons = "coffee:";
    public const string SizeButton = "coffee:size:";
    public const string ConfirmButton = "coffee:confirm";
    public const string CancelButton = "coffee:cancel";

    public static readonly InlineKeyboardMarkup SizeKeyboard = new([
        [
            InlineKeyboardButton.WithCallbackData("Small", SizeButton + "small"),
            InlineKeyboardButton.WithCallbackData("Medium", SizeButton + "medium"),
            InlineKeyboardButton.WithCallbackData("Large", SizeButton + "large"),
        ],
        [InlineKeyboardButton.WithCallbackData("Cancel", CancelButton)],
    ]);

    public static readonly InlineKeyboardMarkup ConfirmKeyboard = new([
        [
            InlineKeyboardButton.WithCallbackData("Confirm", ConfirmButton),
            InlineKeyboardButton.WithCallbackData("Cancel", CancelButton),
        ],
    ]);

    public static CoffeeSize? ParseSize(string data) =>
        data.StartsWith(SizeButton)
        && Enum.TryParse<CoffeeSize>(data[SizeButton.Length..], ignoreCase: true, out var size)
        && Enum.IsDefined(size)
            ? size
            : null;
}

[JsonConverter(typeof(JsonStringEnumConverter<CoffeeSize>))]
internal enum CoffeeSize
{
    Small,
    Medium,
    Large,
}

internal sealed record CoffeeOrder(CoffeeSize? Size = null, string? CupName = null);
