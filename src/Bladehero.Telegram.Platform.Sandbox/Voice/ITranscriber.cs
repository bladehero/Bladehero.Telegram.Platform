namespace Bladehero.Telegram.Platform.Sandbox.Voice;

// Turns speech into text, e.g. through a speech-to-text service; the audio is a Telegram voice message (OGG/Opus).
internal interface ITranscriber
{
    Task<Transcription> TranscribeAsync(byte[] audio, CancellationToken token);
}

// What was said, or why it could not be made out.
internal sealed record Transcription(string? Text, string? Error)
{
    public static Transcription Of(string text) => new(text, Error: null);

    public static Transcription Failure(string error) => new(Text: null, error);
}
