using Bladehero.Telegram.Platform.Receiving.Buttons;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Echo;

// An echo's buttons: Again, and Louder until the reply is as loud as it gets. Again keeps its hand-written data, next
// to Louder's typed one.
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
