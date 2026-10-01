using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Echo;

// An echo's Louder button, "louder:{n}".
[ButtonData("louder")]
internal readonly record struct Louder(int Loudness);

// n from 1 to 3: the reply in capitals with n exclamation marks.
internal sealed class LouderButton : CallbackQueryCommand<Louder>
{
    protected override Task<ButtonCheck> CheckAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) =>
        Task.FromResult(Parsed.Loudness is >= 1 and <= EchoKeyboard.Loudest ? ButtonCheck.Accept : ButtonCheck.Decline);

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var reply = query.Message!;
        var loudness = Parsed.Loudness;

        // Before this tap the reply carries one mark fewer; take those off, then raise it.
        var quiet = reply.Text![..^(loudness - 1)];

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(
            reply.Chat,
            reply.Id,
            quiet.ToUpperInvariant() + new string('!', loudness),
            replyMarkup: EchoKeyboard.At(loudness),
            cancellationToken: token
        );
    }
}
