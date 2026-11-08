using System.Text.Json.Serialization;
using Telegram.Bot.Requests;

namespace Bladehero.Telegram.Platform.Hub;

// The hub's two additions to the Bot API. They go through the bot's own client, so a fake Telegram can answer them.
internal sealed class SetHubAppRequest() : RequestBase<bool>("setHubApp")
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("site")]
    public string? Site { get; init; }

    [JsonPropertyName("takes_groups")]
    public bool TakesGroups { get; init; }

    [JsonPropertyName("takes_sign_in_codes")]
    public bool TakesSignInCodes { get; init; }

    [JsonPropertyName("unlisted_commands")]
    public required IReadOnlyList<string> UnlistedCommands { get; init; }
}

internal sealed class GetHubPeopleRequest() : RequestBase<HubPersonDto[]>("getHubPeople");

internal sealed class HubPersonDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
