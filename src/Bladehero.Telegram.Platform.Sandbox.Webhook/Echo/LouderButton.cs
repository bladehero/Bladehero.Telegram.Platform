using System.Globalization;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Echo;

// "louder:{n}", n from 1 to 3: the reply in capitals with n exclamation marks.
internal sealed class LouderButton : CallbackQueryCommand<int>
{
    protected override int? Parse(string data) =>
        data.StartsWith(EchoKeyboard.LouderPrefix, StringComparison.Ordinal)
        && int.TryParse(
            data[EchoKeyboard.LouderPrefix.Length..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var loudness
        )
        && loudness is >= 1 and <= EchoKeyboard.Loudest
            ? loudness
            : null;

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (_, query, client) = request;
        var reply = query.Message!;

        // Before this tap the reply carries one mark fewer; take those off, then raise it.
        var quiet = reply.Text![..^(Parsed - 1)];

        await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
        await client.EditMessageText(
            reply.Chat,
            reply.Id,
            quiet.ToUpperInvariant() + new string('!', Parsed),
            replyMarkup: EchoKeyboard.At(Parsed),
            cancellationToken: token
        );
    }
}
