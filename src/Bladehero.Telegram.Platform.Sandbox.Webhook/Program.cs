using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;

var builder = WebApplication.CreateBuilder(args);

// The mode decides what is registered, so it has to be read before Build.
var byWebhook = !string.IsNullOrWhiteSpace(builder.Configuration["Telegram:BaseUrl"]);
if (byWebhook)
{
    builder.Services.AddTelegramWebhookReceiving(
        builder.Configuration,
        "Telegram",
        assemblies: typeof(Program).Assembly
    );
}
else
{
    builder.Services.AddTelegramLongPollingReceiving(
        builder.Configuration,
        "Telegram",
        assemblies: typeof(Program).Assembly
    );
}

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
if (byWebhook)
{
    app.UseTelegramWebhook();
}

app.Run();
