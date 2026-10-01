namespace Bladehero.Telegram.Platform.Sandbox.Voice;

// A demo transcriber, with CoffeeShop:Demo on: every voice message asks for a large coffee.
internal sealed class DemoTranscriber : ITranscriber
{
    public Task<Transcription> TranscribeAsync(byte[] audio, CancellationToken token) =>
        Task.FromResult(Transcription.Of("A large coffee, please"));
}
