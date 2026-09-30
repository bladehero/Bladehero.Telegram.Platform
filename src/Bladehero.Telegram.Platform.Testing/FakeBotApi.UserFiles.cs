using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing;

// Files a user sends, stored as the Telegram app sends them: for TestUser, and for hand-built updates.
public sealed partial class FakeBotApi
{
    /// <summary>Stores a user's photo for a hand-built update; returns its sizes, smallest first.</summary>
    /// <exception cref="ArgumentException"><paramref name="photo"/> is empty.</exception>
    public PhotoSize[] StoreUserPhoto(byte[] photo)
    {
        ThrowIfEmpty(photo);

        return UserPhoto(photo)["photo"].Deserialize<PhotoSize[]>(JsonBotAPI.Options)!;
    }

    /// <summary>Stores a user's document; the MIME type comes from the extension unless given.</summary>
    /// <exception cref="ArgumentException"><paramref name="content"/> is empty, or <paramref name="fileName"/> blank.</exception>
    public Document StoreUserDocument(byte[] content, string fileName, string? mimeType = null)
    {
        ThrowIfEmpty(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return UserDocument(content, fileName, mimeType)["document"].Deserialize<Document>(JsonBotAPI.Options)!;
    }

    /// <summary>Stores a user's voice message of <paramref name="duration"/> (one second by default).</summary>
    /// <exception cref="ArgumentException"><paramref name="voice"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public Voice StoreUserVoice(byte[] voice, TimeSpan? duration = null)
    {
        ThrowIfEmpty(voice);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration ?? TimeSpan.Zero, TimeSpan.Zero, nameof(duration));

        return UserVoice(voice, duration)["voice"].Deserialize<Voice>(JsonBotAPI.Options)!;
    }

    // In Telegram's four sizes, smallest first; only the largest downloads as the photo.
    internal JsonObject UserPhoto(byte[] photo) =>
        StoreFile(FileKind.Photo, photo, new JsonObject { ["width"] = 1280, ["height"] = 960 });

    internal JsonObject UserDocument(byte[] content, string fileName, string? mimeType) =>
        StoreFile(
            FileKind.Document,
            content,
            new JsonObject
            {
                ["file_name"] = fileName,
                ["mime_type"] = string.IsNullOrWhiteSpace(mimeType) ? MimeTypes.Of(fileName) : mimeType,
            }
        );

    internal JsonObject UserVoice(byte[] voice, TimeSpan? duration) =>
        StoreFile(
            FileKind.Voice,
            voice,
            new JsonObject
            {
                ["duration"] = (int)Math.Ceiling((duration ?? TimeSpan.FromSeconds(1)).TotalSeconds),
                ["mime_type"] = "audio/ogg",
            }
        );

    internal static void ThrowIfEmpty(byte[] content, [CallerArgumentExpression(nameof(content))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(content, name);

        if (content.Length == 0)
        {
            throw new ArgumentException("The Telegram app never sends an empty file.", name);
        }
    }
}
