using System.Globalization;
using System.Text.Json.Serialization;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

internal static class CoffeeFlow
{
    public const string Name = "coffee";

    public const string SizeStep = "size";
    public const string NameStep = "name";
    public const string ConfirmStep = "confirm";

    public const string SizeAction = "size";
    public const string ConfirmAction = "confirm";
    public const string CancelAction = "cancel";

    // Unique enough within one person's orders, and short enough to keep the callback data under 64 bytes.
    public static string NewOrderId() => Guid.NewGuid().ToString("N")[..12];

    public static InlineKeyboardMarkup SizeKeyboard(long ownerId, string orderId) =>
        new([
            [
                .. Enum.GetValues<CoffeeSize>()
                    .Select(size =>
                        InlineKeyboardButton.WithCallbackData(
                            size.ToString(),
                            new CoffeeButton(SizeAction, ownerId, orderId, size).ToString()
                        )
                    ),
            ],
            [CancelButton(ownerId, orderId)],
        ]);

    public static InlineKeyboardMarkup NameKeyboard(long ownerId, string orderId) =>
        new([
            [CancelButton(ownerId, orderId)],
        ]);

    public static InlineKeyboardMarkup ConfirmKeyboard(long ownerId, string orderId) =>
        new([
            [
                InlineKeyboardButton.WithCallbackData(
                    "Confirm",
                    new CoffeeButton(ConfirmAction, ownerId, orderId).ToString()
                ),
                CancelButton(ownerId, orderId),
            ],
        ]);

    public static CoffeeSize? ParseSize(string? text) =>
        Enum.TryParse<CoffeeSize>(text, ignoreCase: true, out var size) && Enum.IsDefined(size) ? size : null;

    // The first size a sentence names, e.g. "A large one, please".
    public static CoffeeSize? SizeIn(string text) =>
        text.Split([' ', ',', '.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseSize)
            .FirstOrDefault(size => size is not null);

    // Whether the button is on the tapper's own card of the order in progress; a stale or someone else's is not.
    public static async Task<bool> IsCurrentAsync(
        IConversation conversation,
        CoffeeButton button,
        long tapperId,
        CancellationToken token
    ) => button.OwnerId == tapperId && (await conversation.GetDataAsync<CoffeeOrder>(token))?.OrderId == button.OrderId;

    private static InlineKeyboardButton CancelButton(long ownerId, string orderId) =>
        InlineKeyboardButton.WithCallbackData("Cancel", new CoffeeButton(CancelAction, ownerId, orderId).ToString());
}

// A coffee button's data, "coffee:{action}:{owner}:{order}[:{size}]": whose order it is, and which one.
internal readonly record struct CoffeeButton(string Action, long OwnerId, string OrderId, CoffeeSize? Size = null)
{
    public static CoffeeButton? Parse(string? data) =>
        data?.Split(':') switch
        {
            [CoffeeFlow.Name, var action, var owner, var order] when long.TryParse(owner, out var ownerId) =>
                new CoffeeButton(action, ownerId, order),
            [CoffeeFlow.Name, var action, var owner, var order, var size]
                when long.TryParse(owner, out var ownerId) && CoffeeFlow.ParseSize(size) is { } parsed =>
                new CoffeeButton(action, ownerId, order, parsed),
            _ => null,
        };

    public override string ToString()
    {
        var data = string.Create(CultureInfo.InvariantCulture, $"{CoffeeFlow.Name}:{Action}:{OwnerId}:{OrderId}");
        return Size is { } size ? $"{data}:{size.ToString().ToLowerInvariant()}" : data;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<CoffeeSize>))]
internal enum CoffeeSize
{
    Small,
    Medium,
    Large,
}

// The conversation's data: the order, and the card showing its buttons now.
internal sealed record CoffeeOrder(string OrderId, int CardId, CoffeeSize? Size = null, string? CupName = null);
