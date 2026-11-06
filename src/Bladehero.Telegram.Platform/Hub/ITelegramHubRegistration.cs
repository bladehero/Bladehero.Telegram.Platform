namespace Bladehero.Telegram.Platform.Hub;

// What an app tells its bot once, at its start, and a hub has to hear again: it may have been down then, or have lost its
// file since. Telegram keeps these, so only an app behind a hub ever asks.
internal interface ITelegramHubRegistration
{
    Task RepeatAsync(CancellationToken cancellationToken);
}
