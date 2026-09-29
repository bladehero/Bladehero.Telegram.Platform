namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Translator;

// Translates text, e.g. through a translation service.
internal interface ITranslator
{
    Task<Translation> TranslateAsync(string text, CancellationToken token);
}

// The translated text, or why there is none.
internal sealed record Translation(string? Text, string? Error)
{
    public static Translation Of(string text) => new(text, Error: null);

    public static Translation Failure(string error) => new(Text: null, error);
}
