# Bladehero.Telegram.Platform

[![NuGet](https://img.shields.io/nuget/v/Bladehero.Telegram.Platform.svg)](https://www.nuget.org/packages/Bladehero.Telegram.Platform/)
[![Downloads](https://img.shields.io/nuget/dt/Bladehero.Telegram.Platform.svg)](https://www.nuget.org/packages/Bladehero.Telegram.Platform/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A small framework for building Telegram bots on .NET in a **command-style** approach.

[Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) gives you one `Update` object and a `switch`
statement. This library gives you a place to put your code: every piece of bot behaviour is a **command** —
an ordinary DI-registered class that says whether it can handle an update and then handles it. Commands are
discovered by assembly scanning, resolved from the container, and dispatched in parallel. Long polling and
webhook hosting are drop-in, so the same commands run either way.

```csharp
public sealed class EchoCommand : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text is not null);

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        await client.SendMessage(message.Chat, $"Reply: {message.Text}", cancellationToken: token);
    }
}
```

That is the whole contract. No registration call, no routing table — drop the class in a scanned assembly
and it participates.

## Packages

| Package | What it gives you |
| --- | --- |
| [`Bladehero.Telegram.Platform`](https://www.nuget.org/packages/Bladehero.Telegram.Platform/) | `TelegramBotConfiguration` and an `ITelegramBotClient` registration for `IServiceCollection`. |
| [`Bladehero.Telegram.Platform.Receiving`](https://www.nuget.org/packages/Bladehero.Telegram.Platform.Receiving/) | The command model: `ITelegramCommand`, typed base commands, assembly scanning, the parallel executor, error handling. |
| [`Bladehero.Telegram.Platform.Receiving.Background`](https://www.nuget.org/packages/Bladehero.Telegram.Platform.Receiving.Background/) | Hosting: a long-polling `BackgroundService` and an ASP.NET Core webhook endpoint that keeps the Telegram webhook registration in sync. |

Referencing `.Receiving.Background` pulls in the other two. Target framework is **.NET 10**.

```sh
dotnet add package Bladehero.Telegram.Platform.Receiving.Background
```

## Quick start — long polling

Best for local development and bots that do not need a public URL. The bot pulls updates from Telegram.

```csharp
using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (context, services) =>
        {
            services.AddTelegramLongPollingReceiving(context.Configuration, assemblies: typeof(Program).Assembly);
        }
    )
    .Build();

await host.RunAsync();
```

`appsettings.json`:

```json
{
  "TelegramReceiverConfiguration": {
    "Token": "123456:ABC-DEF...",
    "AllowedUpdates": ["Message", "CallbackQuery"],
    "DropPendingUpdates": true
  }
}
```

## Quick start — webhook

Best for production. Telegram pushes updates to an endpoint you expose.

```csharp
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTelegramWebhookReceiving(builder.Configuration, assemblies: typeof(Program).Assembly);

var app = builder.Build();
app.UseTelegramWebhook();
app.Run();
```

`appsettings.json`:

```json
{
  "TelegramWebhookConfiguration": {
    "Token": "123456:ABC-DEF...",
    "BaseUrl": "https://bot.example.com",
    "UpdateEndpoint": "telegram/updates",
    "AllowedUpdates": ["Message"],
    "DropPendingUpdates": false
  }
}
```

`UseTelegramWebhook()` maps `POST {UpdateEndpoint}` and feeds every update through the same command pipeline
as long polling. On startup a hosted service compares the live webhook registration against your
configuration and only calls `setWebhook` when the URL, allowed updates, or pending-update handling actually
differ — so restarts are cheap and you are not rate-limited for redundant registrations.

The token is a secret: keep it in user secrets, environment variables, or your secret store rather than in
`appsettings.json`. Both sample projects use `dotnet user-secrets`.

## Configuration

The section name defaults to the configuration type's own name — `TelegramReceiverConfiguration` for long
polling and `TelegramWebhookConfiguration` for webhooks. Pass `sectionName` to override it:

```csharp
services.AddTelegramLongPollingReceiving(
    configuration,
    sectionName: "MyBot",
    assemblies: typeof(Program).Assembly
);
```

| Property | Applies to | Description |
| --- | --- | --- |
| `Token` | both | Bot token from [@BotFather](https://t.me/BotFather). **Required.** |
| `AllowedUpdates` | both | `UpdateType` values to subscribe to. Omit for Telegram's default set. |
| `DropPendingUpdates` | both | Discard updates that queued up while the bot was down. |
| `Offset` | long polling | Update id to resume from. |
| `Limit` | long polling | Max updates per poll. |
| `BaseUrl` | webhook | Public origin Telegram will call, e.g. `https://bot.example.com`. **Required.** |
| `UpdateEndpoint` | webhook | Path the update endpoint is mapped on, e.g. `telegram/updates`. **Required.** |

## Commands

### Typed commands

`TypedTelegramCommand<T>` binds to one `UpdateType` and unwraps the payload for you, so `CanHandleAsync` and
`HandleAsync` receive the concrete type rather than a raw `Update`. Derive from the base class matching the
update you care about:

| Base class | Payload | Update type |
| --- | --- | --- |
| `MessageCommand` | `Message` | `Message` |
| `EditedMessageCommand` | `Message` | `EditedMessage` |
| `ChannelPostCommand` | `Message` | `ChannelPost` |
| `EditedChannelPostCommand` | `Message` | `EditedChannelPost` |
| `CallbackQueryCommand` | `CallbackQuery` | `CallbackQuery` |
| `InlineQueryCommand` | `InlineQuery` | `InlineQuery` |
| `ChosenInlineResultCommand` | `ChosenInlineResult` | `ChosenInlineResult` |
| `PollCommand` | `Poll` | `Poll` |
| `PollAnswerCommand` | `PollAnswer` | `PollAnswer` |
| `ShippingQueryCommand` | `ShippingQuery` | `ShippingQuery` |
| `PreCheckoutQueryCommand` | `PreCheckoutQuery` | `PreCheckoutQuery` |
| `MyChatMemberCommand` | `ChatMemberUpdated` | `MyChatMember` |
| `ChatMemberCommand` | `ChatMemberUpdated` | `ChatMember` |
| `ChatJoinRequestCommand` | `ChatJoinRequest` | `ChatJoinRequest` |

`CanHandleAsync` is where routing lives. Because it is async and gets the full payload, "which command runs"
can depend on anything — the message text, the callback data, the user's state in your database:

```csharp
public sealed class StartCommand(IUserRepository users) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text?.StartsWith("/start", StringComparison.Ordinal) is true);

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;
        await users.EnsureRegisteredAsync(message.From!.Id, token);
        await client.SendMessage(message.Chat, "Welcome aboard 👋", cancellationToken: token);
    }
}
```

Commands are registered as **scoped** services, so constructor-inject whatever you need — repositories,
`ILogger<T>`, `HttpClient`, your own services.

`TypedCommandRequest<T>` carries `UpdateId`, `Payload`, and `Client`, and deconstructs into all three.

### Raw commands

For anything the typed bases do not cover, implement `ITelegramCommand` directly and work with the whole
`Update`:

```csharp
public sealed class AuditCommand(ILogger<AuditCommand> logger) : ITelegramCommand
{
    public Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) => Task.FromResult(true);

    public Task HandleAsync(CommandRequest request, CancellationToken token)
    {
        logger.LogInformation("Update {Id} of type {Type}", request.Update.Id, request.Update.Type);
        return Task.CompletedTask;
    }
}
```

`CommandRequest` exposes `Update` and `Client` and deconstructs into both.

## Execution model

For each update the executor asks every command whether it can handle it, then runs all commands that said
yes. **Commands are not mutually exclusive** — several can handle the same update, which is what makes
cross-cutting behaviour (logging, analytics, rate limiting) just another command rather than middleware.

Commands are processed in chunks, `CanHandleAsync` and `HandleAsync` running in parallel within a chunk.
Chunk size defaults to 5 and is configurable:

```csharp
services.Configure<ParallelCommandExecutionConfiguration>(options => options.ParallelCount = 10);
```

Set `ParallelCount` to `null` to run every command in a single unbounded batch.

To replace dispatch entirely, register your own `ITelegramCommandExecutor` after `AddTelegramReceiving`.

## Error handling

Exceptions surfaced by the receiver go to `ITelegramErrorHandler`. The default implementation logs them and
ignores the cancellation-shaped `RequestException` that long polling raises on shutdown. Override it with
your own registration:

```csharp
services.AddScoped<ITelegramErrorHandler, SentryTelegramErrorHandler>();
```

## Customizing the HttpClient

Every `Add…` entry point takes an optional `httpClientFactory`, used to build the `HttpClient` behind
`ITelegramBotClient`. Useful for proxies, forcing IPv4, retry handlers, or logging:

```csharp
services.AddTelegramLongPollingReceiving(
    configuration,
    httpClientFactory: _ => new HttpClient(
        new SocketsHttpHandler { ConnectCallback = Ipv4OnlyConnectCallback }
    ),
    assemblies: typeof(Program).Assembly
);
```

## Samples

Two runnable projects live in this repository:

- [`src/Bladehero.Telegram.Platform.Sandbox`](src/Bladehero.Telegram.Platform.Sandbox) — long-polling console
  host with a command that logs `MyChatMember` updates.
- [`src/Bladehero.Telegram.Platform.Sandbox.Webhook`](src/Bladehero.Telegram.Platform.Sandbox.Webhook) —
  ASP.NET Core webhook host with a command that echoes messages back.

Set a token and run:

```sh
cd src/Bladehero.Telegram.Platform.Sandbox
dotnet user-secrets set "TelegramReceiverConfiguration:Token" "123456:ABC-DEF..."
dotnet run
```

## License

[MIT](LICENSE)
