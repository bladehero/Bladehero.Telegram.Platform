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
| [`Bladehero.Telegram.Platform`](https://www.nuget.org/packages/Bladehero.Telegram.Platform/) | `TelegramBotConfiguration`, `ITelegramSender`, and the `IServiceCollection` wiring behind both. |
| [`Bladehero.Telegram.Platform.Receiving`](https://www.nuget.org/packages/Bladehero.Telegram.Platform.Receiving/) | The command model: `ITelegramCommand`, typed base commands, assembly scanning, the parallel executor, multi-step conversations, error handling. |
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

Telegram serves a bot through `getUpdates` **or** a webhook, never both — polling a bot that still has one
registered fails with HTTP 409 on every attempt. On startup the host checks for a webhook and deletes one
if it finds it, logging the URL it removed. Worth knowing if the bot you are polling locally is the same
bot serving a deployed webhook: starting long polling will unregister it.

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

### Bot commands

`IsCommand` recognises a slash command in every form Telegram sends it — bare, `@`-qualified, and with
arguments — so `CanHandleAsync` stays a one-liner:

```csharp
protected override bool Matches(Message message) => message.IsCommand("/last");
```

`ArgumentsOf("/last")` returns whatever followed the command, or `null` when there was nothing.

### Commands for known users only

Most bots serve people they already know about. `KnownUserCommand<TUser>` resolves the chat through your
`ITelegramUserResolver<TUser>` and runs only when that succeeds, handing the resolved user to `HandleAsync`:

```csharp
internal sealed class LastExpensesCommand(IExpenseQueries expenses) : KnownUserCommand<User>
{
    protected override bool Matches(Message message) => message.IsCommand("/last");

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var recent = await expenses.RecentAsync(User.Id, token);   // User is already resolved
        await request.Client.SendMessage(request.Payload.Chat, Render(recent), cancellationToken: token);
    }
}
```

Register the resolver once — `services.AddScoped<ITelegramUserResolver<User>, UserResolver>()` — and every
known-user command receives it; the constructor stays free for the command's own dependencies.

An unresolved chat makes the command decline the update rather than throw, so a stranger messaging the bot
is ignored instead of raising an error for every message. The resolver is keyed on the chat id rather than
the message, so the same implementation serves the buttons below.

### Buttons with typed data

A button press arrives as a callback query carrying up to 64 bytes of data — usually a tiny vocabulary such as
`page:2` or `open:<id>`. `CallbackQueryCommand<TData>` turns it into a typed value once, while deciding whether to
handle the update: `Parse` returns the value, or `null` for a button that belongs to another command, and
`HandleAsync` reads it from `Parsed`. A tuple carries several fields:

```csharp
internal sealed class ExpenseButtonCommand(IExpenses expenses) : KnownUserCallbackQueryCommand<User, (string Action, Guid Id)>
{
    protected override (string Action, Guid Id)? Parse(string data) =>
        data.Split(':') is [var action, var id] && action is "edit" or "delete" && Guid.TryParse(id, out var expenseId)
            ? (action, expenseId)
            : null;

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        await request.Client.AnswerCallbackQuery(request.Payload.Id, cancellationToken: token);

        if (Parsed.Action == "delete")
        {
            await expenses.DeleteAsync(User.Id, Parsed.Id, token);
        }
    }
}
```

`KnownUserCallbackQueryCommand<TUser, TData>` is the known-user flavour: it parses first, so a button meant for
another command never costs a lookup, and then resolves the chat the button sits in. `CallbackQueryCommand<TData>`
is the same without a user.

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

To replace dispatch entirely, register your own `ITelegramCommandExecutor` after `AddTelegramReceiving` — it then
owns [conversation](#conversations) routing too.

### Scopes

Every update is handled in its own DI scope under both hosting models — webhook updates get the ASP.NET Core
request scope, and long polling creates one per update. Scoped dependencies therefore behave the way you
would expect in a controller: a fresh instance per update, disposed once the update finishes.

Commands within a single update share that scope and, by default, run in parallel. If they share a scoped
dependency that is not thread-safe — an EF Core `DbContext` being the usual one — either set `ParallelCount`
to `1` so commands run one at a time, or resolve a scope of your own inside the command.

## Conversations

Some exchanges take more than one message: a sign-up asking for a name and then a city, a form with a button per
field, a confirmation before something irreversible. Between those messages the bot has to remember where the user
is. A **conversation** is that memory — the flow it runs, the step it waits at, and the data gathered so far — and a
**step** is an ordinary command that only runs while the sender's conversation is at it:

```csharp
public sealed record Signup(string Name);

public sealed class SignupCommand(IConversation conversation) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/signup"));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        await conversation.StartAsync("signup", "name", token);
        await request.Client.SendMessage(request.Payload.Chat, "What's your name?", cancellationToken: token);
    }
}

[ConversationStep("signup", "name")]
public sealed class SignupNameStep(IConversation conversation) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text?.StartsWith('/') is false);

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        await conversation.MoveToAsync("city", new Signup(Name: request.Payload.Text!), token);
        await request.Client.SendMessage(request.Payload.Chat, "Which city?", cancellationToken: token);
    }
}

[ConversationStep("signup", "city")]
public sealed class SignupCityStep(IConversation conversation, IUserRepository users) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text?.StartsWith('/') is false);

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var signup = await conversation.GetDataAsync<Signup>(token);
        await users.RegisterAsync(signup!.Name, request.Payload.Text!, token);
        await conversation.EndAsync(token);
        await request.Client.SendMessage(request.Payload.Chat, "Welcome aboard 👋", cancellationToken: token);
    }
}
```

Steps are found by the same assembly scan as every other command and keep everything a command has — typed
payloads and button data, `KnownUserCommand<TUser>`, priorities, constructor injection. How an update is routed:

- **While a conversation is active, its steps go first.** Commands marked with the conversation's flow and step —
  or with the flow alone, `[ConversationStep("signup")]`, to run at any step — see the update before any regular
  command, and when one handles it the regular commands are skipped. The flow owns the update.
- **When every step declines, the update falls through** to the regular commands. That is how `/cancel` and `/help`
  keep working mid-flow, and why a step taking free text should decline bot commands, as above — otherwise it
  swallows the `/cancel` meant to end it.
- **Without a conversation, steps never run.**
- **Changes steer the next update.** Starting, moving or ending a conversation while handling an update affects
  where the following update goes; the current one keeps the routing it started with.

A conversation belongs to one user in one chat, so in a group every member runs their own and one member's button
press never advances another's flow. `IConversation` is scoped to the update and carries typed data as JSON through
`StartAsync`, `MoveToAsync` and `GetDataAsync<T>`.

Conversations live in memory by default and a restart forgets the ones in progress. Register an
`IConversationStore` to persist them — it replaces the built-in store whichever order you register it in. The store
is also the way to open a conversation outside of an update, when a background job finishes, say:

```csharp
await store.SaveAsync(new ConversationKey(chatId, userId), new ConversationState("import", "describe"), token);
```

Bots without a single step never touch the store: the feature costs nothing until a command carries
`[ConversationStep]`.

## Sending on your own initiative

A command answering an update already holds the bot client — it arrives on the request, so replying never
needs anything from the container. Messages the bot starts by itself have no request to draw on: a
reminder, a nightly digest, an alert raised by a background job. Those go through `ITelegramSender`:

```csharp
public sealed class LimitAlerts(ITelegramSender sender)
{
    public Task WarnAsync(long chatId, string text, CancellationToken token) =>
        sender.SendAsync(chatId, text, cancellationToken: token);
}
```

`ITelegramBotClient` itself is **not** registered in the container, on purpose. Two ways to reach the same
object invites the wrong one — a command injecting the client instead of using its request, and losing the
distinction between "the bot replied" and "the bot spoke first". The library builds the client internally and
hands it to commands on their request; everything else sends through `ITelegramSender`.

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
  host with a command that logs `MyChatMember` updates, and a `/coffee` [conversation](#conversations) that
  exercises every routing rule: text and button steps, a Cancel button at any step, `/cancel` falling through
  mid-flow, and stale buttons answered after the order closes.
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
