using Bladehero.Telegram.Platform.Sandbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    // Run without launch settings, the host is in Production, where user secrets are not loaded by default.
    .ConfigureAppConfiguration((_, config) => config.AddUserSecrets<Program>())
    .ConfigureServices((context, services) => services.AddCoffeeShop(context.Configuration))
    .Build();

await host.RunAsync();
