using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook;

public sealed class SendMessageBackCommand : MessageCommand
{
    internal const string AgainData = "again";

    private static readonly InlineKeyboardMarkup AgainKeyboard = new(
        InlineKeyboardButton.WithCallbackData("Again", AgainData)
    );

    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(true);

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        await client.SendMessage(
            message.Chat,
            $"Reply: {message.Text}",
            replyMarkup: AgainKeyboard,
            cancellationToken: token
        );
    }
}
