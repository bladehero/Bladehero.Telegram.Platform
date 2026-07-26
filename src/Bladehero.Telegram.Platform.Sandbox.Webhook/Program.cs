using Bladehero.Telegram.Platform.Receiving.Background.Webhook;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>();
builder.Services.AddTelegramWebhookReceiving(builder.Configuration, assemblies: typeof(Program).Assembly);
var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.UseTelegramWebhook();
app.Run();
