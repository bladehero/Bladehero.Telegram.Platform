using Bladehero.Telegram.Platform.History;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Sandbox.Activity;

// The chat's latest history, one line per entry. The /recent itself is the last: it's recorded before it's handled.
[BotCommand("recent", "What happened here lately")]
internal sealed class RecentCommand(ITelegramHistory history) : MessageCommand
{
    private const int LineLength = 80;

    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/recent"));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        var entries = await history.ReadAsync(new() { ChatId = message.Chat.Id, Limit = 10 }, token);
        var lines = entries.Select(x => Shorten(x.ToString()));

        await client.SendMessage(
            message.Chat,
            string.Join('\n', ["Lately in this chat:", .. lines]),
            cancellationToken: token
        );
    }

    private static string Shorten(string line) => line.Length <= LineLength ? line : line[..(LineLength - 1)] + "…";
}
