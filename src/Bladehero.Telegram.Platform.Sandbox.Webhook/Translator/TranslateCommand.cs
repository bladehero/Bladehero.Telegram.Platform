using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Translator;

// "/translate hello". A translator that throws fails the update.
[BotCommand("translate", "Translate text, e.g. /translate hello")]
internal sealed class TranslateCommand(ITranslator translator) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/translate"));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;

        if (message.ArgumentsOf("/translate") is not { } text)
        {
            await client.SendMessage(
                message.Chat,
                "What should I translate? Try /translate hello",
                cancellationToken: token
            );
            return;
        }

        var translation = await translator.TranslateAsync(text, token);
        await client.SendMessage(
            message.Chat,
            translation.Text ?? translation.Error ?? "That didn't translate.",
            cancellationToken: token
        );
    }
}
