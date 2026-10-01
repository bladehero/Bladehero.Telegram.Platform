namespace Bladehero.Telegram.Platform.Testing;

// Blocking the bot in a private chat.
public sealed partial class TestUser
{
    /// <summary>Whether the user blocked the bot in their private chat.</summary>
    public bool HasBlockedBot => _host.Api.HasBlocked(Chat.Id);

    /// <summary>Blocks the bot: its sends to this chat fail with 403 until the user unblocks it.</summary>
    /// <remarks>
    /// The bot gets a <c>my_chat_member</c> update when it asks for one; the block holds either way. Edits, deletions
    /// and answers to taps still go through, which is unverified against Telegram.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is a group, or the user blocked the bot already.</exception>
    public Task BlocksBotAsync(CancellationToken token = default)
    {
        ThrowIfInAGroup();
        if (HasBlockedBot)
        {
            throw new InvalidOperationException($"{FirstName} has blocked the bot already.");
        }

        var update = _host.Api.Block(Person, blocked: true);
        return _host.DeliverIfAllowedAsync("my_chat_member", () => update, token);
    }

    /// <summary>Unblocks the bot; with <paramref name="restart"/>, then sends <c>/start</c>.</summary>
    /// <exception cref="InvalidOperationException">This is a group, or the user hasn't blocked the bot.</exception>
    public async Task UnblocksBotAsync(bool restart = false, CancellationToken token = default)
    {
        ThrowIfInAGroup();
        if (!HasBlockedBot)
        {
            throw new InvalidOperationException($"{FirstName} hasn't blocked the bot.");
        }

        var update = _host.Api.Block(Person, blocked: false);
        await _host.DeliverIfAllowedAsync("my_chat_member", () => update, token);

        if (restart)
        {
            await SendsAsync("/start", token);
        }
    }

    // Before any other action in the chat.
    private void ThrowIfBlocked()
    {
        if (HasBlockedBot)
        {
            throw new InvalidOperationException(
                $"{FirstName} blocked the bot, so the app shows Unblock instead of the message field; call "
                    + "UnblocksBotAsync first."
            );
        }
    }

    private void ThrowIfInAGroup()
    {
        if (Chat.IsGroup)
        {
            throw new InvalidOperationException("Only a private chat can block the bot; a group member can't.");
        }
    }
}
