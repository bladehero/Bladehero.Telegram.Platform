namespace Bladehero.Telegram.Platform.Testing;

// Reacting to a message.
public sealed partial class TestUser
{
    /// <summary>Reacts to <paramref name="message"/> with <paramref name="emoji"/>, or takes the reaction back with <c>null</c>.</summary>
    /// <remarks>
    /// The bot gets a <c>message_reaction</c> update only when it asks for one and, in a group, only as an admin; the
    /// reaction shows either way.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="emoji"/> isn't a reaction Telegram offers.</exception>
    /// <exception cref="InvalidOperationException">
    /// The message is from another chat or no longer in this one, or the bot doesn't ask for reactions.
    /// </exception>
    public Task ReactsAsync(TestMessage message, string? emoji, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (emoji is not null && !FakeBotApi.IsReaction(emoji))
        {
            throw new ArgumentException($"\"{emoji}\" isn't a reaction Telegram offers.", nameof(emoji));
        }

        ThrowIfBlocked();
        var messageId = StillShown(message, Messages).Id;
        _host.Api.ThrowIfNotAllowed("message_reaction");

        // Telegram sends a group's reactions only to an admin.
        if (Chat.IsGroup && !_host.Api.IsAdmin(Chat.Id))
        {
            _host.Api.UserReacts(Chat.Id, messageId, Person, emoji);
            return Task.CompletedTask;
        }

        return _host.DeliverAsync(
            "message_reaction",
            () => _host.Api.UserReacts(Chat.Id, messageId, Person, emoji),
            token
        );
    }
}
