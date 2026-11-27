namespace Bladehero.Telegram.Platform.Hub;

/// <summary>What a person pass says, once checked, for <see cref="HubPassOptions.People"/>.</summary>
/// <param name="TelegramId">The person the pass acts for.</param>
/// <param name="Via">The delegate that asked for it, such as <c>home</c>.</param>
/// <param name="ConnectionId">
/// The fixed id this app gives the person's connection at the delegate,
/// <see cref="HubPassDefaults.ConnectionIdOf"/>: the same on every call and every machine, so a retry with the same
/// request id answers the first save, and a renamed connection keeps its history.
/// </param>
/// <param name="ConnectionName">The connection's name for people, such as <c>Claude · Ник</c>.</param>
/// <param name="Scopes">What the pass carries, cut to the scopes this app marks <c>ForPeople</c>. Never empty.</param>
/// <param name="Services">The request's services, to look the person up with.</param>
public sealed record HubPassPerson(
    long TelegramId,
    string Via,
    Guid ConnectionId,
    string ConnectionName,
    IReadOnlyList<string> Scopes,
    IServiceProvider Services
);
