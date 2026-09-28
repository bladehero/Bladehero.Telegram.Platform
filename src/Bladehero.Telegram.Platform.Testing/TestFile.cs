using System.Text;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A file in a <see cref="TestChat"/> — a photo, voice message or document, sent by a member or by the bot — with the
/// bytes it would download as.
/// </summary>
public sealed class TestFile
{
    private readonly byte[] _content;

    internal TestFile(string id, string? fileName, string? mimeType, byte[] content, string? url)
    {
        _content = content;
        Id = id;
        FileName = fileName;
        MimeType = mimeType;
        Url = url;
    }

    /// <summary>The file's Telegram id, which the bot can send again instead of uploading the file anew.</summary>
    public string Id { get; }

    /// <summary>A document's name; <c>null</c> for photos and voice messages.</summary>
    public string? FileName { get; }

    public string? MimeType { get; }

    /// <summary>
    /// The address the bot sent the file by, for Telegram to fetch; <c>null</c> when it was uploaded or sent by id.
    /// </summary>
    public string? Url { get; }

    /// <summary>The file's bytes.</summary>
    /// <exception cref="InvalidOperationException">
    /// The bot sent the file by <see cref="Url"/>. The fake never goes online to fetch it, so it has no bytes.
    /// </exception>
    public byte[] Content =>
        Url is null
            ? [.. _content]
            : throw new InvalidOperationException(
                $"The bot sent this file by URL, {Url}, and the fake never goes online to fetch it, so it has no "
                    + "content. Check the Url instead."
            );

    /// <summary>The file's bytes read as UTF-8 text.</summary>
    /// <exception cref="InvalidOperationException">The bot sent the file by <see cref="Url"/>.</exception>
    public string ReadAsString() => Encoding.UTF8.GetString(Content);

    public override string ToString() => FileName ?? Url ?? Id;
}
