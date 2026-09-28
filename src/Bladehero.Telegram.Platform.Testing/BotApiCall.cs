using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// One request the bot made to the Bot API: the method, such as <c>sendMessage</c>, and the parameters exactly as
/// they were sent.
/// </summary>
public sealed record BotApiCall(string Method, JsonObject Parameters);
