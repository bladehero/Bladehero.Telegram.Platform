using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Telegram.DotNet.Platform.Receiving.Background;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) => config.AddUserSecrets<Program>())
    .ConfigureServices(
        (context, services) =>
        {
            services.AddTelegramReceivingBackground(context.Configuration, assemblies: typeof(Program).Assembly);
        }
    )
    .Build();

await host.RunAsync();
