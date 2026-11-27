using System.Text.Json.Serialization;
using Telegram.Bot.Requests;

namespace Bladehero.Telegram.Platform.Hub;

// The hub's additions to the Bot API. They go through the bot's own client, so a fake Telegram can answer them.
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

    [JsonPropertyName("scopes")]
    public required IReadOnlyList<HubScopeDto> Scopes { get; init; }

    [JsonPropertyName("claim_patterns")]
    public required IReadOnlyList<string> ClaimPatterns { get; init; }

    [JsonPropertyName("fallback_for")]
    public required IReadOnlyList<string> FallbackFor { get; init; }
}

internal sealed class HubScopeDto
{
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    // Left out unless set, so an app that grants people nothing sends the manifest it sent before.
    [JsonPropertyName("people")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool People { get; init; }
}

internal sealed class GetHubPassRequest() : RequestBase<HubPassDto>("getHubPass")
{
    [JsonPropertyName("audience")]
    public required string Audience { get; init; }

    [JsonPropertyName("scopes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Scopes { get; init; }

    // A delegate's pass acts for this person, through this connection of theirs; an app's own pass names neither.
    [JsonPropertyName("person")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Person { get; init; }

    [JsonPropertyName("connection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HubConnectionDto? Connection { get; init; }
}

internal sealed class HubConnectionDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

internal sealed class GetHubAppsRequest() : RequestBase<HubAppDto[]>("getHubApps")
{
    [JsonPropertyName("person")]
    public required long Person { get; init; }
}

internal sealed class HubAppDto
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("site")]
    public string? Site { get; set; }

    [JsonPropertyName("scopes")]
    public HubAppScopeDto[] Scopes { get; set; } = [];
}

internal sealed class HubAppScopeDto
{
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";
}

internal sealed class HubPassDto
{
    [JsonPropertyName("pass")]
    public string Pass { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";
}

internal sealed class GetHubPeopleRequest() : RequestBase<HubPersonDto[]>("getHubPeople");

internal sealed class HubPersonDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
