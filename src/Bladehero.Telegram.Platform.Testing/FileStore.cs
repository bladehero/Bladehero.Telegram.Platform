using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// The files Telegram keeps: each has an id to pass around in messages and, once a bot asks for it with getFile, a path
// to download it from.
internal sealed class FileStore
{
    private readonly List<StoredFile> _files = [];

    public StoredFile Add(byte[] content, string folder, string extension)
    {
        var number = _files.Count + 1;
        var file = new StoredFile(
            $"file_{number}",
            $"unique_{number}",
            $"{folder}/file_{number}{extension}",
            [.. content]
        );

        _files.Add(file);
        return file;
    }

    public StoredFile? Find(string fileId) => _files.Find(file => file.Id == fileId);

    public StoredFile? AtPath(string path) => _files.Find(file => file.Path == path);
}

internal sealed record StoredFile(string Id, string UniqueId, string Path, byte[] Content)
{
    // How a message refers to the file; the path is only handed out by getFile.
    public JsonObject Describe() =>
        new()
        {
            ["file_id"] = Id,
            ["file_unique_id"] = UniqueId,
            ["file_size"] = Content.Length,
        };
}
