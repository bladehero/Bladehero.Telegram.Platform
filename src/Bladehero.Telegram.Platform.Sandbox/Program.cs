using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) => config.AddUserSecrets<Program>())
    .ConfigureServices(
        (context, services) =>
        {
            services.AddTelegramLongPollingReceiving(context.Configuration, assemblies: typeof(Program).Assembly);
        }
    )
    .Build();

await host.RunAsync();
