using System.Collections.Concurrent;

namespace Bladehero.Telegram.Platform.Sandbox.Receipts;

// Receipt albums being collected, by chat, owner and media group, until their owner taps Read. Telegram sends an
// album's items one by one, and long polling handles one update at a time, so its first item is always first here.
// Kept in memory until taken or the bot restarts; a long-running bot would expire them, or keep them in its database.
internal sealed class ReceiptAlbums
{
    private readonly ConcurrentDictionary<AlbumKey, ReceiptAlbum> _albums = new();

    public ReceiptAlbum Open(AlbumKey key, DateTime sentAt) => _albums.GetOrAdd(key, _ => new ReceiptAlbum(sentAt));

    // Only one of two taps at once gets the album.
    public ReceiptAlbum? Take(AlbumKey key) => _albums.TryRemove(key, out var album) ? album : null;
}

internal readonly record struct AlbumKey(long ChatId, long OwnerId, string GroupId);

internal sealed class ReceiptAlbum(DateTime sentAt)
{
    private readonly object _gate = new();
    private readonly List<ReceiptFile> _files = [];

    public DateTime SentAt { get; } = sentAt;

    // The first caption given; the Telegram apps put it under an album's first photo, or its last file.
    public string? Caption { get; private set; }

    // The message announcing the album, once sent.
    public int? PromptId { get; set; }

    public IReadOnlyList<ReceiptFile> Files
    {
        get
        {
            lock (_gate)
            {
                return [.. _files];
            }
        }
    }

    // The number of pages so far.
    public int Add(ReceiptFile file, string? caption)
    {
        lock (_gate)
        {
            _files.Add(file);
            Caption ??= string.IsNullOrWhiteSpace(caption) ? null : caption;
            return _files.Count;
        }
    }
}
