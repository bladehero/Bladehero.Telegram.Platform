using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// One request the bot made to the Bot API: the method, such as <c>sendMessage</c>, and the parameters exactly as
/// they were sent.
/// </summary>
/// <remarks>
/// A request that uploads a file is sent as a form rather than JSON, so its fields are recorded as text, as Telegram
/// receives them — <c>"42"</c> for a chat id, <c>"True"</c> for a flag — and each uploaded file as
/// <c>attach://</c> and the name of the form part carrying it. Only the fields Telegram reads as JSON, such as
/// <c>reply_markup</c>, are recorded as JSON. To check what an upload sent, read the file from the chat's messages.
/// </remarks>
public sealed record BotApiCall(string Method, JsonObject Parameters);
