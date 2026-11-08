namespace Bladehero.Telegram.Platform.Hub;

/// <summary>Someone the hub lists for this app.</summary>
/// <param name="Id">Their Telegram id.</param>
/// <param name="Name">Their name, as the hub's list has it.</param>
public sealed record TelegramHubPerson(long Id, string Name);
