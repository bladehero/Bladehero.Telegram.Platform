using System.Collections.Concurrent;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Notes;

// One note per user, kept in memory.
internal sealed class Notebook
{
    public const string Flow = "remember";

    private readonly ConcurrentDictionary<long, string> _notes = new();

    public void Write(long userId, string note) => _notes[userId] = note;

    public string? Read(long userId) => _notes.GetValueOrDefault(userId);
}
