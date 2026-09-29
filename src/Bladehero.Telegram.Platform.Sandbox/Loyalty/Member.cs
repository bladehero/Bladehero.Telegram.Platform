namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// A loyalty club member, known by their Telegram user id.
internal sealed record Member(long UserId, string Name, int Points);
