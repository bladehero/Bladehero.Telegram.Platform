using System.Text;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>A photo, voice message or document in a <see cref="TestChat"/>, with its bytes.</summary>
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

    /// <summary>The Telegram file id, reusable to send the file again.</summary>
    public string Id { get; }

    /// <summary>A document's name; <c>null</c> otherwise.</summary>
    public string? FileName { get; }

    /// <summary>The MIME type of a document or voice message; <c>null</c> for photos.</summary>
    public string? MimeType { get; }

    /// <summary>The URL the file was first sent by (kept when resent by id); <c>null</c> for uploads.</summary>
    public string? Url { get; }

    /// <summary>A copy of the file's bytes.</summary>
    /// <exception cref="InvalidOperationException">
    /// The file was sent by <see cref="Url"/>, which the fake never fetches.
    /// </exception>
    public byte[] Content =>
        Url is null
            ? [.. _content]
            : throw new InvalidOperationException(
                $"The bot sent this file by URL, {Url}, and the fake never goes online to fetch it, so it has no "
                    + "content. Check the Url instead."
            );

    /// <summary>The bytes as UTF-8 text.</summary>
    /// <exception cref="InvalidOperationException">The bot sent the file by <see cref="Url"/>.</exception>
    public string ReadAsString() => Encoding.UTF8.GetString(Content);

    public override string ToString() => FileName ?? Url ?? Id;
}
