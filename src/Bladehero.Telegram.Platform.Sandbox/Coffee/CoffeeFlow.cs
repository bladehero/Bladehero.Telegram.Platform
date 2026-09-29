using System.Text.Json.Serialization;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// Every button of an order is bound to its conversation, so the library answers a tap from another customer, or from
// an earlier order, before any step runs.
internal static class CoffeeFlow
{
    public const string Name = "coffee";

    public const string SizeStep = "size";
    public const string NameStep = "name";
    public const string ConfirmStep = "confirm";

    public static InlineKeyboardMarkup SizeKeyboard(ConversationBinding binding)
    {
        var keyboard = new InlineKeyboardMarkup();
        foreach (var size in Enum.GetValues<CoffeeSize>())
        {
            keyboard.AddButton(size.ToString(), new PickSize(size), binding);
        }

        return keyboard.AddNewRow().AddButton("Cancel", new CancelOrder(), binding);
    }

    public static InlineKeyboardMarkup NameKeyboard(ConversationBinding binding) =>
        new InlineKeyboardMarkup().AddButton("Cancel", new CancelOrder(), binding);

    public static InlineKeyboardMarkup ConfirmKeyboard(ConversationBinding binding) =>
        new InlineKeyboardMarkup()
            .AddButton("Confirm", new ConfirmOrder(), binding)
            .AddButton("Cancel", new CancelOrder(), binding);

    public static string ConfirmText(CoffeeOrder order) =>
        $"A {order.Size} coffee for {order.CupName}. Place the order?";

    public static CoffeeSize? ParseSize(string? text) =>
        Enum.TryParse<CoffeeSize>(text, ignoreCase: true, out var size) && Enum.IsDefined(size) ? size : null;

    // The first size a sentence names, e.g. "A large one, please".
    public static CoffeeSize? SizeIn(string text) =>
        text.Split([' ', ',', '.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseSize)
            .FirstOrDefault(size => size is not null);
}

// "coffee-size:large@7000000001.k3j9x2ab"
[ButtonData("coffee-size")]
internal readonly record struct PickSize(CoffeeSize Size);

[ButtonData("coffee-confirm")]
internal readonly record struct ConfirmOrder();

[ButtonData("coffee-cancel")]
internal readonly record struct CancelOrder();

[JsonConverter(typeof(JsonStringEnumConverter<CoffeeSize>))]
internal enum CoffeeSize
{
    Small,
    Medium,
    Large,
}

// The conversation's data: the card showing its buttons now, and the message the cup name came in.
internal sealed record CoffeeOrder(
    int CardId,
    CoffeeSize? Size = null,
    string? CupName = null,
    int? NameMessageId = null
);
