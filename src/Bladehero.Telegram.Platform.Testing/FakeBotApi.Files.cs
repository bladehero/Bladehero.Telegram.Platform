using System.Net;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Files both ways: what users send the bot, served back through getFile and a download, and what the bot sends —
// uploaded, by the id of a file Telegram already has, or by URL.
public sealed partial class FakeBotApi
{
    private const string AttachPrefix = "attach://";

    // The fields of an upload that carry JSON rather than plain text.
    private static readonly HashSet<string> JsonFormFields =
    [
        "reply_markup",
        "caption_entities",
        "entities",
        "reply_parameters",
        "link_preview_options",
    ];

    // Keeps a file a person sends, returning the part of their message that carries it.
    internal JsonObject StoreFile(FileKind kind, byte[] content, JsonObject details)
    {
        lock (_gate)
        {
            return _files.Add(kind, content, details).ToMessageContent();
        }
    }

    internal TestFile File(string fileId)
    {
        lock (_gate)
        {
            var file =
                _files.Find(fileId)
                ?? throw new InvalidOperationException($"Telegram has no file {fileId} — it was never sent.");
            var described = file.Describe();

            return new TestFile(
                file.Id,
                described["file_name"]?.GetValue<string>(),
                described["mime_type"]?.GetValue<string>(),
                file.Content,
                file.Url
            );
        }
    }

    // Bots can only download files of up to 20 MB; Telegram refuses to hand out a path to a bigger one.
    private JsonObject GetFile(JsonObject parameters)
    {
        var file =
            (parameters["file_id"]?.GetValue<string>() is { } fileId ? _files.Find(fileId) : null)
            ?? throw Refuse(400, "Bad Request: invalid file_id");

        if (file.Url is not null)
        {
            throw Refuse(
                400,
                $"Bad Request: the bot sent this file by URL, and FakeBotApi never goes online to fetch {file.Url}"
            );
        }

        if (file.Content.Length > DownloadLimit)
        {
            throw Refuse(400, "Bad Request: file is too big");
        }

        file.PathGiven = true;

        var info = file.Describe();
        info["file_path"] = file.Path;
        return info;
    }

    // Only a path getFile handed out can be downloaded; any other is not found, as on Telegram's file server.
    private HttpResponseMessage Download(string path)
    {
        StoredFile? file;
        lock (_gate)
        {
            file = _files.AtPath(Uri.UnescapeDataString(path)) is { PathGiven: true } given ? given : null;
        }

        return file is null
            ? Respond(new BotApiError(404, "Not Found"))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Content) };
    }

    private JsonObject SendFile(
        JsonObject parameters,
        IReadOnlyDictionary<string, Attachment> attachments,
        FileKind kind
    )
    {
        var chat = ChatOf(parameters);
        var content = FileFor(kind, parameters, attachments).ToMessageContent();

        if (NonBlank(parameters["caption"]?.GetValue<string>()) is { } caption)
        {
            content["caption"] = caption;
        }

        if (InlineKeyboardOf(parameters) is { } keyboard)
        {
            content["reply_markup"] = keyboard;
        }

        return chat.Post(Bot(), content).DeepClone().AsObject();
    }

    // The file a send refers to: an upload, attached to the request as attach://<part>; a URL for Telegram to fetch;
    // or the id of a file Telegram already has, which must be of the same kind.
    private StoredFile FileFor(
        FileKind kind,
        JsonObject parameters,
        IReadOnlyDictionary<string, Attachment> attachments
    )
    {
        var field = kind.ToString().ToLowerInvariant();
        var value =
            parameters[field]?.GetValue<string>()
            ?? throw Refuse(400, $"Bad Request: there is no {field} in the request");

        if (value.StartsWith(AttachPrefix, StringComparison.Ordinal))
        {
            var attachment =
                attachments.GetValueOrDefault(value[AttachPrefix.Length..])
                ?? throw Refuse(400, "Bad Request: wrong file identifier/HTTP URL specified");

            return attachment.Content.Length == 0
                ? throw Refuse(400, "Bad Request: file must be non-empty")
                : _files.Add(kind, attachment.Content, DetailsOf(kind, attachment.FileName, parameters));
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var url) && (url.Scheme == "http" || url.Scheme == "https"))
        {
            return _files.Add(kind, [], DetailsOf(kind, Path.GetFileName(url.AbsolutePath), parameters), value);
        }

        var known = _files.Find(value) ?? throw Refuse(400, "Bad Request: wrong file identifier/HTTP URL specified");
        return known.Kind == kind ? known : throw Refuse(400, "Bad Request: type of file mismatch");
    }

    // What Telegram tells about a file the bot sends. It does not look inside the bytes, so a photo's dimensions are
    // nominal and a voice message lasts as long as the bot says.
    private static JsonObject DetailsOf(FileKind kind, string fileName, JsonObject parameters) =>
        kind switch
        {
            FileKind.Photo => new JsonObject { ["width"] = 1280, ["height"] = 960 },
            FileKind.Voice => new JsonObject
            {
                ["duration"] = NumberOf(parameters["duration"]) ?? 0,
                ["mime_type"] = "audio/ogg",
            },
            _ => new JsonObject { ["file_name"] = fileName, ["mime_type"] = MimeTypes.Of(fileName) },
        };

    // A request's parameters: its JSON body, or the fields of an upload with the uploaded files kept apart.
    private static async Task<(JsonObject Parameters, IReadOnlyDictionary<string, Attachment> Attachments)> ReadAsync(
        HttpContent? content,
        CancellationToken token
    )
    {
        if (content is MultipartFormDataContent form)
        {
            var parameters = new JsonObject();
            var attachments = new Dictionary<string, Attachment>();

            foreach (var part in form)
            {
                var disposition = part.Headers.ContentDisposition;
                if (disposition?.Name?.Trim('"') is not { } name)
                {
                    continue;
                }

                if (disposition.FileName?.Trim('"') is { } fileName)
                {
                    attachments[name] = new Attachment(fileName, await part.ReadAsByteArrayAsync(token));
                }
                else
                {
                    var text = await part.ReadAsStringAsync(token);
                    parameters[name] = JsonFormFields.Contains(name) ? JsonNode.Parse(text) : text;
                }
            }

            return (parameters, attachments);
        }

        var body = content is null ? "" : await content.ReadAsStringAsync(token);
        return (
            string.IsNullOrWhiteSpace(body) ? [] : JsonNode.Parse(body)!.AsObject(),
            new Dictionary<string, Attachment>()
        );
    }

    private sealed record Attachment(string FileName, byte[] Content);
}
