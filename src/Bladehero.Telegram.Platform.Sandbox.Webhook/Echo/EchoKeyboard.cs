using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Echo;

// An echo's buttons: Again, with hand-written data, and a typed Louder until the reply is as loud as it gets.
internal static class EchoKeyboard
{
    public const string AgainData = "again";
    public const int Loudest = 3;

    public static InlineKeyboardMarkup At(int loudness)
    {
        var again = InlineKeyboardButton.WithCallbackData("Again", AgainData);
        if (loudness >= Loudest)
        {
            return new InlineKeyboardMarkup(again);
        }

        var louder = ButtonData.Button("Louder", new Louder(loudness + 1));
        return new InlineKeyboardMarkup([
            [again, louder],
        ]);
    }
}
