using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// The files Telegram keeps: each has an id to pass around in messages and, once a bot asks for it with getFile, a path
// to download it from.
internal sealed class FileStore
{
    private const int LongestExtension = 20;

    private readonly List<StoredFile> _files = [];

    public StoredFile Add(byte[] content, string folder, string extension)
    {
        var number = _files.Count + 1;
        var file = new StoredFile(
            $"file_{number}",
            $"unique_{number}",
            $"{folder}/file_{number}{PathExtension(extension)}",
            [.. content]
        );

        _files.Add(file);
        return file;
    }

    public StoredFile? Find(string fileId) => _files.Find(file => file.Id == fileId);

    public StoredFile? AtPath(string path) => _files.Find(file => file.Path == path);

    // Paths look like Telegram's own, such as documents/file_12.pdf: only ASCII letters and digits of the extension are
    // kept, so no name the user gave the file can turn the download URL into something else — "?" into a query, "#"
    // into a fragment.
    private static string PathExtension(string extension)
    {
        var kept = new string([.. extension.Where(char.IsAsciiLetterOrDigit).Take(LongestExtension)]);
        return kept.Length == 0 ? "" : $".{kept}";
    }
}

internal sealed class StoredFile(string id, string uniqueId, string path, byte[] content)
{
    public string Id { get; } = id;

    public string UniqueId { get; } = uniqueId;

    public string Path { get; } = path;

    public byte[] Content { get; } = content;

    // Whether getFile has handed out the path, which is what makes the file downloadable.
    public bool PathGiven { get; set; }

    // How a message refers to the file; the path is only handed out by getFile.
    public JsonObject Describe() =>
        new()
        {
            ["file_id"] = Id,
            ["file_unique_id"] = UniqueId,
            ["file_size"] = Content.Length,
        };
}
