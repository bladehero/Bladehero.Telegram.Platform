namespace Bladehero.Telegram.Platform.Sandbox.Voice;

// The default until a real transcriber is registered: the bot still answers, and says why it cannot listen.
internal sealed class DisabledTranscriber : ITranscriber
{
    public Task<Transcription> TranscribeAsync(byte[] audio, CancellationToken token) =>
        Task.FromResult(Transcription.Failure("Voice orders are not set up."));
}
