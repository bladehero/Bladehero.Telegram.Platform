using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Telegram.DotNet.Platform.Receiving.Background.LongPolling;

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
