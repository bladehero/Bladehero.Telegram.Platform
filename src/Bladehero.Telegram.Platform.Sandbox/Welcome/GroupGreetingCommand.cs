using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.MyChatMembers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Sandbox.Welcome;

// Greets a group when the bot is added to it. Being removed, or promoted where it already was, says nothing.
internal sealed class GroupGreetingCommand : MyChatMemberCommand
{
    protected override Task<bool> CanHandleAsync(
        TypedCommandRequest<ChatMemberUpdated> request,
        CancellationToken token
    )
    {
        var update = request.Payload;
        return Task.FromResult(
            update.Chat.Type is ChatType.Group or ChatType.Supergroup
                && update.OldChatMember.Status is ChatMemberStatus.Left or ChatMemberStatus.Kicked
                && update.NewChatMember.Status is ChatMemberStatus.Member or ChatMemberStatus.Administrator
        );
    }

    protected override Task HandleAsync(TypedCommandRequest<ChatMemberUpdated> request, CancellationToken token)
    {
        var (_, update, client) = request;
        return client.SendMessage(
            update.Chat,
            $"Hi {update.Chat.Title}! Send /coffee to order.",
            cancellationToken: token
        );
    }
}
