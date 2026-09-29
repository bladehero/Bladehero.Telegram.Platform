using System.Text;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Sandbox.Loyalty;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

[BotCommand("history", "Your receipts as a CSV file")]
internal sealed class HistoryCommand(ReceiptHistory history) : KnownUserCommand<Member>
{
    protected override bool Matches(Message message) => message.IsCommand("/history");

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var receipts = history.Of(User.UserId);

        if (receipts.Count == 0)
        {
            await client.SendMessage(message.Chat, "No receipts yet.", cancellationToken: token);
            return;
        }

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(ReceiptHistory.ToCsv(receipts)));
        await client.SendDocument(
            message.Chat,
            InputFile.FromStream(csv, "receipts.csv"),
            caption: "Your receipts",
            cancellationToken: token
        );
    }
}
