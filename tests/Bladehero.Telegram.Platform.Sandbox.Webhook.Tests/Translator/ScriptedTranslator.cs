using System.Collections.Concurrent;
using Bladehero.Telegram.Platform.Sandbox.Webhook.Translator;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests.Translator;

// Stands in for the translation service: records the texts it was given, and answers with Translation or throws
// Failure.
internal sealed class ScriptedTranslator : ITranslator
{
    private readonly ConcurrentQueue<string> _texts = new();

    public Translation Translation { get; init; } = Translation.Of("hola");

    public Exception? Failure { get; init; }

    public IReadOnlyList<string> Texts => [.. _texts];

    public Task<Translation> TranslateAsync(string text, CancellationToken token)
    {
        _texts.Enqueue(text);
        return Failure is null ? Task.FromResult(Translation) : Task.FromException<Translation>(Failure);
    }
}
