namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Translator;

// The default until a real translator is registered: the bot still answers, and says why it cannot translate.
internal sealed class DisabledTranslator : ITranslator
{
    public Task<Translation> TranslateAsync(string text, CancellationToken token) =>
        Task.FromResult(Translation.Failure("Translation is not set up."));
}
