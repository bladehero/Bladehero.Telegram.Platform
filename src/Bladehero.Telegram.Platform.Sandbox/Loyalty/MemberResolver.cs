using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// By the sender's user id, not the chat, so a member is known in every chat, groups included.
internal sealed class MemberResolver(MemberDirectory members) : ITelegramUserResolver<Member>
{
    public Task<Member?> ResolveAsync(long chatId, long userId, CancellationToken token) =>
        Task.FromResult(members.Find(userId));
}
