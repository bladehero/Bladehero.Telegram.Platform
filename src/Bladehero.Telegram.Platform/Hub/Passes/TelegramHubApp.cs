namespace Bladehero.Telegram.Platform.Hub;

/// <summary>An app a delegate may reach for a person, as <c>getHubApps</c> answers.</summary>
/// <param name="Slug">Its slug, the audience of its passes, such as <c>gym</c>.</param>
/// <param name="Title">Its title in the hub.</param>
/// <param name="Site">Its site, if it has one.</param>
/// <param name="Scopes">
/// What the person may grant the delegate there: the app's scopes for people, cut to the owner's cap for the delegate.
/// Each has <see cref="TelegramHubScope.ForPeople"/> set.
/// </param>
public sealed record TelegramHubApp(string Slug, string Title, string? Site, IReadOnlyList<TelegramHubScope> Scopes);
