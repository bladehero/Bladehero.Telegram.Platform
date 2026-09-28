using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// The files Telegram keeps: each has an id to pass around in messages and, once a bot asks for it with getFile, a path
// to download it from.
internal sealed class FileStore
{
    private const int LongestExtension = 20;

    private readonly List<StoredFile> _files = [];

    // Details are what a message says about the file beyond its id and size: a photo's dimensions, a document's name
    // and MIME type, a voice message's duration.
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

    // Paths look like Telegram's own, such as documents/file_12.pdf: only ASCII letters and digits of the extension are
    // kept, so no name the user gave the file can turn the download URL into something else — "?" into a query, "#"
    // into a fragment.
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

    // Set when the bot sent the file by URL: Telegram would fetch it, but the fake never goes online, so it has no
    // content.
    public string? Url { get; } = url;

    // Whether getFile has handed out the path, which is what makes the file downloadable.
    public bool PathGiven { get; set; }

    // How a message refers to the file; the path is only handed out by getFile.
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

    // The part of a message that carries the file: a photo comes as its sizes, the rest as one object.
    public JsonObject ToMessageContent() =>
        Kind switch
        {
            FileKind.Photo => new JsonObject { ["photo"] = new JsonArray(Describe()) },
            FileKind.Voice => new JsonObject { ["voice"] = Describe() },
            _ => new JsonObject { ["document"] = Describe() },
        };
}
