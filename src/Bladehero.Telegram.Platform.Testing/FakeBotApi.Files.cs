using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Files both ways: user files served through getFile and download; bot files uploaded, reused by id, or sent by URL.
public sealed partial class FakeBotApi
{
    private const string AttachPrefix = "attach://";

    // Upload form fields that carry JSON rather than text.
    private static readonly HashSet<string> JsonFormFields =
    [
        "reply_markup",
        "caption_entities",
        "reply_parameters",
        "suggested_post_parameters",
        "allowed_updates",
    ];

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    internal JsonObject StoreFile(FileKind kind, byte[] content, JsonObject details)
    {
        lock (_gate)
        {
            return _files.Add(kind, content, details).ToMessageContent();
        }
    }

    internal TestFile TestFileOf(string fileId)
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

    // Bots may only download files up to 20 MB.
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

    // Only paths handed out by getFile can be downloaded.
    private HttpResponseMessage Download(string path)
    {
        StoredFile? file;
        lock (_gate)
        {
            file = _files.AtPath(Uri.UnescapeDataString(path)) is { PathGiven: true } given ? given : null;
        }

        return file is null
            ? Respond(new BotApiError(404, "Not Found"))
            : FailedDownload(file.Id)
                ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Content) };
    }

    private JsonObject SendFile(
        JsonObject parameters,
        IReadOnlyDictionary<string, Attachment> attachments,
        FileKind kind
    )
    {
        var chat = ChatOf(parameters);
        var (caption, entities) = Trimmed(parameters["caption"]?.GetValue<string>(), parameters["caption_entities"]);
        ThrowIfLongerThan(CaptionLimit, caption, "Bad Request: message caption is too long");
        var keyboard = InlineKeyboardOf(parameters);

        // Stored only once the request passed every other check, so a refused one changes nothing.
        var content = FileFor(kind, parameters, attachments).ToMessageContent();

        if (caption is not null)
        {
            content["caption"] = caption;

            if (entities is not null)
            {
                content["caption_entities"] = entities;
            }
        }

        if (keyboard is not null)
        {
            content["reply_markup"] = keyboard;
        }

        return WithReplyMarkup(chat, parameters, chat.Post(Bot(), content)).DeepClone().AsObject();
    }

    // An upload (attach://<part>), a URL, or the id of a known file of the same kind.
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
            // Unescaped after splitting, so "%2F" stays part of the name.
            var fileName = Uri.UnescapeDataString(url.AbsolutePath[(url.AbsolutePath.LastIndexOf('/') + 1)..]);
            return _files.Add(kind, [], DetailsOf(kind, fileName.Length == 0 ? "file" : fileName, parameters), value);
        }

        var known = _files.Find(value) ?? throw Refuse(400, "Bad Request: wrong file identifier/HTTP URL specified");
        return known.Kind == kind ? known : throw Refuse(400, "Bad Request: type of file mismatch");
    }

    // The fake never inspects bytes: photo dimensions are nominal and a voice lasts as long as the bot says.
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

                if (FileNameOf(disposition) is { } fileName)
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

    // Telegram.Bot writes a name's UTF-8 bytes one char per byte, which is UTF-8 on the wire; decode them the same way.
    // The raw parameter is read because FileName would also decode RFC 2047-looking names.
    internal static string? FileNameOf(ContentDispositionHeaderValue disposition)
    {
        if (disposition.FileNameStar is { } decoded)
        {
            return decoded;
        }

        var raw = disposition.Parameters.FirstOrDefault(parameter =>
            parameter.Name.Equals("filename", StringComparison.OrdinalIgnoreCase)
        );
        if (raw?.Value?.Trim('"') is not { } fileName)
        {
            return null;
        }

        if (fileName.Any(character => character > 0xFF))
        {
            return fileName;
        }

        try
        {
            return StrictUtf8.GetString(Encoding.Latin1.GetBytes(fileName));
        }
        catch (DecoderFallbackException)
        {
            return fileName;
        }
    }

    private sealed record Attachment(string FileName, byte[] Content);
}
