using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Hub;

internal sealed partial class TelegramHubClient(
    ITelegramBotClient client,
    IOptions<TelegramBotConfiguration> bot,
    IOptions<TelegramHubConfiguration> configuration
) : ITelegramHub
{
    private volatile IReadOnlyList<TelegramHubPerson>? _people;
    private User? _me;

    public bool KnowsPeople => _people is not null;

    public IReadOnlyList<TelegramHubPerson> People => _people ?? [];

    public TelegramHubPerson? FindPerson(long telegramId) => People.FirstOrDefault(person => person.Id == telegramId);

    public async ValueTask<string> SignInLinkAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        _me ??= await client.GetMe(cancellationToken);
        var start = SlugOf(bot.Value.Token) is { } slug ? $"{slug}-{code}" : code;
        return $"https://t.me/{_me.Username}?start={start}";
    }

    // A hub's key is <bot id>:<slug>_<hex>; a token from @BotFather has capitals and doesn't match.
    internal static string? SlugOf(string token) =>
        HubKey().Match(token) is { Success: true } match ? match.Groups["slug"].Value : null;

    internal Task SendManifestAsync(CancellationToken cancellationToken)
    {
        var manifest = configuration.Value;
        return client.SendRequest(
            new SetHubAppRequest
            {
                Title = manifest.Title,
                Site = manifest.Site,
                TakesGroups = manifest.TakesGroups,
                TakesSignInCodes = manifest.TakesSignInCodes,
                UnlistedCommands = manifest.UnlistedCommands,
            },
            cancellationToken
        );
    }

    // Whether the hub listed anyone. Nobody at all is a mistake in its settings, not a household: acting on it would sign
    // everyone out, so it counts as no answer.
    internal async Task<bool> ReadPeopleAsync(CancellationToken cancellationToken)
    {
        var people = await client.SendRequest(new GetHubPeopleRequest(), cancellationToken);
        if (people.Length == 0)
        {
            return false;
        }

        _people = people.Select(person => new TelegramHubPerson(person.Id, person.Name)).ToArray();
        return true;
    }

    [GeneratedRegex("^[0-9]+:(?<slug>[a-z0-9-]+)_[0-9a-f]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HubKey();
}
