using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>A Bot API request the bot made: its method (e.g. <c>sendMessage</c>) and parameters as sent.</summary>
/// <remarks>
/// File uploads are forms: their fields are recorded as text (<c>"42"</c>, <c>"True"</c>) except JSON ones such as
/// <c>reply_markup</c>, and files as <c>attach://&lt;part&gt;</c>. Read uploaded files from the chat instead.
/// </remarks>
public sealed record BotApiCall(string Method, JsonObject Parameters);
