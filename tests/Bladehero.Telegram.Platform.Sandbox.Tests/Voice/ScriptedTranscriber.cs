using System.Collections.Concurrent;
using Bladehero.Telegram.Platform.Sandbox.Voice;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Voice;

// Stands in for speech-to-text: records the audio it was given, and answers with Transcription.
internal sealed class ScriptedTranscriber : ITranscriber
{
    private readonly ConcurrentQueue<byte[]> _heard = new();

    public Transcription Transcription { get; init; } = Transcription.Of("A coffee, please");

    public IReadOnlyList<byte[]> Heard => [.. _heard];

    public Task<Transcription> TranscribeAsync(byte[] audio, CancellationToken token)
    {
        _heard.Enqueue(audio);
        return Task.FromResult(Transcription);
    }
}
