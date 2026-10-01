namespace Bladehero.Telegram.Platform.Testing;

// Replying to a message.
public sealed partial class TestUser
{
    /// <summary>Sends <paramref name="text"/> as a reply to <paramref name="message"/>.</summary>
    /// <returns>The message as posted.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is blank, or longer than the 4096 characters of a message.
    /// </exception>
    /// <exception cref="InvalidOperationException">The message is from another chat, or no longer in this one.</exception>
    public Task<TestMessage> RepliesAsync(TestMessage message, string text, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        text = CheckedText(text);

        return SendsTextAsync(text, StillShown(message, Messages).Id, token);
    }
}
