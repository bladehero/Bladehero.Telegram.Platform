using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Stored files: an id for messages, and a download path once getFile hands it out.
internal sealed class FileStore
{
    private const int LongestExtension = 20;

    private readonly List<StoredFile> _files = [];

    // Details: a photo's dimensions, a document's name and MIME type, a voice's duration.
    public StoredFile Add(FileKind kind, byte[] content, JsonObject details, string? url = null)
    {
        var number = _files.Count + 1;
        var extension = kind switch
        {
            FileKind.Photo => ".jpg",
            FileKind.Voice => ".oga",
            _ => PathExtension(Path.GetExtension(details["file_name"]?.GetValue<string>() ?? "")),
        };
        var folder = kind switch
        {
            FileKind.Photo => "photos",
            FileKind.Voice => "voice",
            _ => "documents",
        };

        var file = new StoredFile(
            $"file_{number}",
            $"unique_{number}",
            kind,
            $"{folder}/file_{number}{extension}",
            [.. content],
            details.DeepClone().AsObject(),
            url
        );

        _files.Add(file);
        return file;
    }

    public StoredFile? Find(string fileId) => _files.Find(file => file.Id == fileId);

    public StoredFile? AtPath(string path) => _files.Find(file => file.Path == path);

    // Only ASCII letters and digits of the extension are kept, so a name cannot add a query or fragment to the URL.
    private static string PathExtension(string extension)
    {
        var kept = new string([.. extension.Where(char.IsAsciiLetterOrDigit).Take(LongestExtension)]);
        return kept.Length == 0 ? "" : $".{kept}";
    }
}

internal enum FileKind
{
    Photo,
    Voice,
    Document,
}

internal sealed class StoredFile(
    string id,
    string uniqueId,
    FileKind kind,
    string path,
    byte[] content,
    JsonObject details,
    string? url
)
{
    public string Id { get; } = id;

    public FileKind Kind { get; } = kind;

    public string Path { get; } = path;

    public byte[] Content { get; } = content;

    // Set for files sent by URL, which have no content: the fake never goes online.
    public string? Url { get; } = url;

    public bool PathGiven { get; set; }

    public JsonObject Describe()
    {
        var described = new JsonObject { ["file_id"] = Id, ["file_unique_id"] = uniqueId };

        if (Url is null)
        {
            described["file_size"] = Content.Length;
        }

        foreach (var (key, value) in details)
        {
            described[key] = value?.DeepClone();
        }

        return described;
    }

    public JsonObject ToMessageContent() =>
        Kind switch
        {
            FileKind.Photo => new JsonObject { ["photo"] = new JsonArray(Describe()) },
            FileKind.Voice => new JsonObject { ["voice"] = Describe() },
            _ => new JsonObject { ["document"] = Describe() },
        };
}
