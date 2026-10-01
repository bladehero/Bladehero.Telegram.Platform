namespace Bladehero.Telegram.Platform.Testing;

// The bot's admin rights in a group.
public sealed partial class TestChat
{
    /// <summary>Whether the bot is an admin of this group.</summary>
    public bool BotIsAdmin => _host.Api.IsAdmin(Id);

    /// <summary>Makes the bot an admin of this group.</summary>
    /// <param name="canDeleteMessages">Whether it may delete others' messages.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <remarks>
    /// An admin gets every message, as with privacy off. The bot gets a <c>my_chat_member</c> update when it asks for
    /// one; the rights hold either way.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is a private chat.</exception>
    public Task MakesBotAdminAsync(bool canDeleteMessages = true, CancellationToken token = default)
    {
        ThrowIfPrivate();

        var update = _host.Api.SetAdmin(Id, admin: true, canDeleteMessages);
        return _host.DeliverIfAllowedAsync("my_chat_member", () => update, token);
    }

    /// <summary>Takes the bot's admin rights away (administrator → member).</summary>
    /// <exception cref="InvalidOperationException">This is a private chat, or the bot isn't an admin.</exception>
    public Task DemotesBotAsync(CancellationToken token = default)
    {
        ThrowIfPrivate();
        if (!BotIsAdmin)
        {
            throw new InvalidOperationException($"The bot isn't an admin in {Description}.");
        }

        var update = _host.Api.SetAdmin(Id, admin: false, canDeleteMessages: false);
        return _host.DeliverIfAllowedAsync("my_chat_member", () => update, token);
    }

    private void ThrowIfPrivate()
    {
        if (!_isGroup)
        {
            throw new InvalidOperationException($"Only a group has admins; this is the chat with {_name}.");
        }
    }
}
