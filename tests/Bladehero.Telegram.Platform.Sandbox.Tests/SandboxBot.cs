using System.Globalization;
using Bladehero.Telegram.Platform.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Sandbox.Tests;

internal static class SandboxBot
{
    // The coffee shop as Program composes it, with test settings and stand-ins registered last, so they win.
    public static Task<TelegramTestHost> StartAsync(
        FakeBotApi? api = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<HostApplicationBuilder>? configure = null
    ) =>
        TelegramTestHost.ForLongPollingAsync(
            builder =>
            {
                builder.Configuration.AddInMemoryCollection([new("TelegramReceiverConfiguration:Token", "unused")]);
                builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
                builder.Services.AddCoffeeShop(builder.Configuration);
                configure?.Invoke(builder);
            },
            api
        );

    public static Task<TelegramTestHost> StartWithMembersAsync(params (string FirstName, int Points)[] members) =>
        StartWithMembersAsync(configure: null, members);

    // With loyalty club members already in, each the test user of that name.
    public static Task<TelegramTestHost> StartWithMembersAsync(
        Action<HostApplicationBuilder>? configure,
        params (string FirstName, int Points)[] members
    )
    {
        var api = new FakeBotApi();
        return StartAsync(api, Members(api, members), configure);
    }

    // A stand-in for an external service; registered after the coffee shop's disabled one, it wins.
    public static Action<HostApplicationBuilder> Using<TService>(TService standIn)
        where TService : class => builder => builder.Services.AddSingleton(standIn);

    // Loyalty club members to seed, each the test user of that name.
    public static Dictionary<string, string?> Members(
        FakeBotApi api,
        params (string FirstName, int Points)[] members
    ) => Members([.. members.Select(x => (api.UserIdOf(x.FirstName), x.FirstName, x.Points))]);

    // Loyalty club members to seed, as CoffeeShop:Members settings.
    public static Dictionary<string, string?> Members(params (long UserId, string Name, int Points)[] members) =>
        members
            .SelectMany(
                (member, i) =>
                    new Dictionary<string, string?>
                    {
                        [$"CoffeeShop:Members:{i}:UserId"] = member.UserId.ToString(CultureInfo.InvariantCulture),
                        [$"CoffeeShop:Members:{i}:Name"] = member.Name,
                        [$"CoffeeShop:Members:{i}:Points"] = member.Points.ToString(CultureInfo.InvariantCulture),
                    }
            )
            .ToDictionary();
}
