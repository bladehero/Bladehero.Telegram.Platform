# Bladehero.Telegram.Platform

[![NuGet](https://img.shields.io/nuget/v/Bladehero.Telegram.Platform.svg)](https://www.nuget.org/packages/Bladehero.Telegram.Platform/)
[![Downloads](https://img.shields.io/nuget/dt/Bladehero.Telegram.Platform.svg)](https://www.nuget.org/packages/Bladehero.Telegram.Platform/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Command-style Telegram bots on .NET 10. Every piece of behaviour is a **command**: a DI-registered class that says
whether it handles an update, then handles it. Commands are found by assembly scanning and run the same under long
polling or a webhook.

```csharp
public sealed class EchoCommand : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text is not null);

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        request.Client.SendMessage(request.Payload.Chat, $"Reply: {request.Payload.Text}", cancellationToken: token);
}
```

No registration, no routing table: drop the class in a scanned assembly.

## Contents

- [Packages](#packages)
- [Quick start](#quick-start): [long polling](#long-polling) · [webhook](#webhook) · [configuration](#configuration)
- [Commands](#commands): [typed](#typed-commands) · [raw](#raw-commands) · [slash commands](#slash-commands) ·
  [command menu](#command-menu) · [known users](#known-users) · [buttons with typed data](#buttons-with-typed-data)
- [Execution](#execution): [priorities](#priorities) · [parallelism](#parallelism) · [scopes](#scopes)
- [Conversations](#conversations)
- [Sending on your own](#sending-on-your-own)
- [Errors and the HttpClient](#errors-and-the-httpclient)
- [Component tests](#component-tests): [start](#start-the-bot) · [configure](#configure-the-app-under-test) ·
  [chat](#chat-with-it) · [taps](#tap-buttons) · [files](#send-and-read-files) ·
  [fake Telegram](#check-and-fail-telegram)
- [Samples](#samples)

## Packages

| Package | Contents |
| --- | --- |
| [`Bladehero.Telegram.Platform`](https://www.nuget.org/packages/Bladehero.Telegram.Platform/) | Bot configuration, `ITelegramSender`, DI wiring. |
| [`Bladehero.Telegram.Platform.Receiving`](https://www.nuget.org/packages/Bladehero.Telegram.Platform.Receiving/) | Commands, scanning, execution, conversations, command menu, error handling. |
| [`Bladehero.Telegram.Platform.Receiving.Background`](https://www.nuget.org/packages/Bladehero.Telegram.Platform.Receiving.Background/) | Long-polling and webhook hosting; startup sync of webhook and menu. |
| [`Bladehero.Telegram.Platform.Testing`](https://www.nuget.org/packages/Bladehero.Telegram.Platform.Testing/) | [Component tests](#component-tests) against an in-memory Telegram. |

```sh
dotnet add package Bladehero.Telegram.Platform.Receiving.Background   # pulls in the other runtime packages
dotnet add package Bladehero.Telegram.Platform.Testing                # test projects
```

## Quick start

### Long polling

The bot pulls updates; no public URL needed.

```csharp
using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
        services.AddTelegramLongPollingReceiving(context.Configuration, assemblies: typeof(Program).Assembly))
    .Build();

await host.RunAsync();
```

```json
{ "TelegramReceiverConfiguration": { "Token": "123456:ABC-DEF..." } }
```

A bot can't poll while it has a webhook, so startup deletes one if found — including one a deployed copy of the bot
relies on.

### Webhook

Telegram pushes updates to your endpoint.

```csharp
using Bladehero.Telegram.Platform.Receiving.Background.Webhook;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTelegramWebhookReceiving(builder.Configuration, assemblies: typeof(Program).Assembly);

var app = builder.Build();
app.UseTelegramWebhook();   // maps POST {UpdateEndpoint}
app.Run();
```

```json
{
  "TelegramWebhookConfiguration": {
    "Token": "123456:ABC-DEF...",
    "BaseUrl": "https://bot.example.com",
    "UpdateEndpoint": "telegram/updates"
  }
}
```

Startup calls `setWebhook` only when the URL or settings changed. Keep the token in user secrets or environment
variables.

### Configuration

Bind a section — named after the type unless you pass `sectionName` — or configure in code, optionally with up to five
resolved dependencies:

```csharp
services.AddTelegramLongPollingReceiving(configuration, sectionName: "MyBot", assemblies: typeof(Program).Assembly);

services.AddTelegramLongPollingReceiving<ISecrets>(
    (receiver, secrets) => receiver.Token = secrets.BotToken,
    typeof(Program).Assembly
);
```

| Property | Mode | |
| --- | --- | --- |
| `Token` | both | Bot token from [@BotFather](https://t.me/BotFather). **Required.** |
| `AllowedUpdates` | both | `UpdateType`s to receive; Telegram's default set when omitted. |
| `DropPendingUpdates` | both | Discard updates queued while the bot was down. |
| `SyncCommandMenu` | both | Publish the [command menu](#command-menu) on startup. Default `true`. |
| `CommandMenuScope` | both | `Default`, `AllPrivateChats`, `AllGroupChats` or `AllChatAdministrators`. |
| `Offset`, `Limit` | polling | Update id to resume from; max updates per poll. |
| `BaseUrl`, `UpdateEndpoint` | webhook | Public origin and endpoint path. **Required.** |

## Commands

### Typed commands

Derive from the base for the update you handle; `CanHandleAsync` and `HandleAsync` get the typed payload.
Commands are **scoped**, so inject anything.

```csharp
public sealed class StartCommand(IUserRepository users) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/start"));

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var (_, message, client) = request;   // UpdateId, Payload, Client
        await users.EnsureRegisteredAsync(message.From!.Id, token);
        await client.SendMessage(message.Chat, "Welcome aboard 👋", cancellationToken: token);
    }
}
```

| Base | Payload |
| --- | --- |
| `MessageCommand`, `EditedMessageCommand`, `ChannelPostCommand`, `EditedChannelPostCommand` | `Message` |
| `CallbackQueryCommand` | `CallbackQuery` |
| `InlineQueryCommand`, `ChosenInlineResultCommand` | `InlineQuery`, `ChosenInlineResult` |
| `PollCommand`, `PollAnswerCommand` | `Poll`, `PollAnswer` |
| `ShippingQueryCommand`, `PreCheckoutQueryCommand` | `ShippingQuery`, `PreCheckoutQuery` |
| `MyChatMemberCommand`, `ChatMemberCommand` | `ChatMemberUpdated` |
| `ChatJoinRequestCommand` | `ChatJoinRequest` |

### Raw commands

Implement `ITelegramCommand` to see the whole `Update`; `CommandRequest` deconstructs into `Update` and `Client`:

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

### Slash commands

```csharp
message.IsCommand("/last");      // true for /last, /last@MyBot and /last 10
message.ArgumentsOf("/last");    // "10", or null
```

### Command menu

Mark a command with `[BotCommand]` to list it in the menu Telegram shows on `/`:

```csharp
[BotCommand("start", "Start over", Order = 1)]
public sealed class StartCommand : MessageCommand { /* … */ }

[BotCommand("help", "What I can do")]
public sealed class HelpCommand(IBotCommandMenu menu) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.IsCommand("/help"));

    protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        request.Client.SendMessage(
            request.Payload.Chat,
            string.Join('\n', menu.Commands.Select(c => $"/{c.Command} - {c.Description}")),
            cancellationToken: token
        );
}
```

- **Order:** ordered commands first (lowest first, unique), then the rest alphabetically.
- **Validated at startup:** unique names of 1–32 `a-z0-9_`, descriptions up to 256 characters, at most 100 commands.
- **Synced at startup** only when it differs. Without any `[BotCommand]` the menu is never touched.
- **Settings:** turn `SyncCommandMenu` off where another environment shares the token. `CommandMenuScope` defaults to
  `Default`; Telegram keeps a menu per scope, so switching scopes leaves the old menu in place.

### Known users

`KnownUserCommand<TUser>` runs only for chats your `ITelegramUserResolver<TUser>` recognises; strangers are ignored.

```csharp
services.AddScoped<ITelegramUserResolver<User>, UserResolver>();

internal sealed class LastExpensesCommand(IExpenseQueries expenses) : KnownUserCommand<User>
{
    protected override bool Matches(Message message) => message.IsCommand("/last");

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        var recent = await expenses.RecentAsync(User.Id, token);   // User is resolved
        await request.Client.SendMessage(request.Payload.Chat, Render(recent), cancellationToken: token);
    }
}
```

### Buttons with typed data

`CallbackQueryCommand<TData>` parses callback data once; `Parse` returns `null` for another command's buttons.
`KnownUserCallbackQueryCommand<TUser, TData>` adds the resolved user.

```csharp
internal sealed class ExpenseButton(IExpenses expenses) : KnownUserCallbackQueryCommand<User, (string Action, Guid Id)>
{
    protected override (string Action, Guid Id)? Parse(string data) =>
        data.Split(':') is [var action and ("edit" or "delete"), var id] && Guid.TryParse(id, out var expenseId)
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

## Execution

Every command whose `CanHandleAsync` returns `true` runs — commands are not exclusive, so logging or rate limiting is
just another command.

### Priorities

```csharp
[CommandPriority(0)]       // runs after unmarked commands
[CommandPriority(1, 0)]    // then global 1: group 0 first, ungrouped commands last
```

Commands are batched by priority and batches run in order: unmarked commands, then by global priority, then by group.

### Parallelism

Within a batch, commands run in chunks of `ParallelCount` (default 5; `null` for one unbounded chunk): the commands of
a chunk run in parallel, chunks one after another.

```csharp
services.Configure<ParallelCommandExecutionConfiguration>(options => options.ParallelCount = 10);
```

To replace dispatch entirely, register your own `ITelegramCommandExecutor` **after** the receiving services; it then
owns [conversation](#conversations) routing too.

### Scopes

Each update gets its own DI scope, shared by its commands. With a non-thread-safe scoped dependency such as a
`DbContext`, set `ParallelCount` to `1` or create a scope inside the command.

## Conversations

Multi-step flows: a **conversation** remembers the flow, the step and the data; a **step** is a command that runs only
while the sender is at it.

```csharp
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
public sealed class SignupNameStep(IConversation conversation, IUserRepository users) : MessageCommand
{
    protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
        Task.FromResult(request.Payload.Text?.StartsWith('/') is false);   // let /cancel fall through

    protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
    {
        await users.RegisterAsync(request.Payload.Text!, token);
        await conversation.EndAsync(token);
        await request.Client.SendMessage(request.Payload.Chat, "Welcome aboard 👋", cancellationToken: token);
    }
}
```

- **Steps go first** while a conversation is active; regular commands run only if every step declines.
- `[ConversationStep("signup")]` without a step runs at any step of the flow.
- **Data:** `StartAsync(flow, step, data)`, `MoveToAsync(step, data)` (or `MoveToAsync(step)` to keep it),
  `GetDataAsync<T>()`, all as JSON.
- **Per user per chat:** each group member has their own conversation.
- A change routes the **next** update, not the current one.
- **Storage:** in memory by default, and untouched by bots without steps. Register an `IConversationStore` to persist
  conversations, or use it to start one from a background job:

```csharp
await store.SaveAsync(new ConversationKey(chatId, userId), new ConversationState("import", "describe"), token);
```

## Sending on your own

Replies use the request's client. Messages the bot starts itself — reminders, alerts — go through `ITelegramSender`.
The receiving setups register it; an app that only sends needs just the core package:

```csharp
services.AddTelegramBot(configuration);   // binds TelegramBotConfiguration: { "Token": "…" }
```

```csharp
public sealed class LimitAlerts(ITelegramSender sender)
{
    public Task WarnAsync(long chatId, string text, CancellationToken token) =>
        sender.SendAsync(chatId, text, cancellationToken: token);
}
```

`ITelegramBotClient` is deliberately not in the container.

## Errors and the HttpClient

Receiver errors go to `ITelegramErrorHandler`, which logs them and ignores the cancellation on shutdown. Each
`TelegramError` carries the `Exception` and the `Update` being handled (`null` for a failed poll), so a handler can
tell the user something went wrong. Replace it by registering your own **after** the receiving services:

```csharp
services.AddScoped<ITelegramErrorHandler, SentryTelegramErrorHandler>();
```

Long polling keeps running after any failure: a command that throws, even a stray `OperationCanceledException` such as
an `HttpClient` timeout, a command graph that can't be built, or an error handler that throws itself (that is logged).

`AddTelegramBot` and the `IConfiguration` overloads of the receiving methods take an `httpClientFactory` for proxies,
IPv4, retries or logging:

```csharp
services.AddTelegramLongPollingReceiving(
    configuration,
    httpClientFactory: _ => new HttpClient(new SocketsHttpHandler { ConnectCallback = Ipv4OnlyConnectCallback }),
    assemblies: typeof(Program).Assembly
);
```

## Component tests

`Bladehero.Telegram.Platform.Testing` runs your bot end to end against an in-memory Telegram: real hosting, no token,
no network, no sleeps.

### Start the bot

A bot on a generic host that long-polls: register it as its composition root does, with any type from its assembly.

```csharp
await using var bot = await TelegramTestHost.ForLongPollingAsync(services =>
    services.AddTelegramLongPollingReceiving(receiver => receiver.Token = "unused", typeof(StartCommand).Assembly));
```

Or take the whole builder, to set configuration, the environment or logging too:

```csharp
await using var bot = await TelegramTestHost.ForLongPollingAsync(builder =>
{
    builder.Configuration.AddInMemoryCollection([new("TelegramReceiverConfiguration:Token", "unused")]);
    builder.Services.AddTelegramLongPollingReceiving(builder.Configuration, assemblies: typeof(StartCommand).Assembly);
});
```

An ASP.NET Core app that long-polls: start its public `Program` via WebApplicationFactory.

```csharp
await using var bot = await TelegramTestHost.ForLongPollingAsync<Program>(web =>
    web.UseSetting("Telegram:Token", "unused"));
```

An ASP.NET Core app with a webhook: the same, and each update is posted to the webhook the app set.

```csharp
await using var bot = await TelegramTestHost.ForWebhookAsync<Program>(web =>
{
    web.UseSetting("Telegram:BaseUrl", "https://bot.example.com");
    web.UseSetting("Telegram:UpdateEndpoint", "telegram/updates");
});
```

All return once startup (webhook, command menu) is done; an app started with the wrong one fails the test and names
the right one. For the web apps the test project references the app; if the factory can't find the app's content root,
also reference `Microsoft.AspNetCore.Mvc.Testing`.

The bot's client talks to the fake, and so does an `ITelegramBotClient` or `TelegramBotClient` the app registers itself,
e.g. for messages it starts.

### Configure the app under test

**Settings.** `web.UseSetting(key, value)` reaches the app as a command-line argument, so it beats everything
`WebApplication.CreateBuilder` loads by default (appsettings, user secrets in Development, environment variables),
values read before `Build()` included. `ConfigureAppConfiguration` only reaches what the app reads after `Build()`.

**Sources the app adds itself.** One added after `CreateBuilder`, such as an explicit `AddUserSecrets`, `AddJsonFile`,
`AddEnvironmentVariables` or a vault, comes after the command line and beats `UseSetting`. Don't re-add sources
`CreateBuilder` already provides, or add `builder.Configuration.AddCommandLine(args)` after your own.

**User secrets.** The app still runs in Development, so the developer's user secrets load underneath, and a key the
test doesn't set comes from them. Set every setting that changes behaviour, even to `""`. The Telegram client is
swapped regardless; replace every other external service, or override its key. `web.UseEnvironment("Testing")` skips
user secrets and `appsettings.Development.json`, at a price: outside Development, the app's container is no longer
validated on build.

**External services**, such as an AI client, get a stand-in. On a generic host, register it after the bot's own
registrations, as the last one wins:

```csharp
await using var bot = await TelegramTestHost.ForLongPollingAsync(services =>
{
    services.AddBudgetBot(configuration);                                     // the app's registrations
    services.AddSingleton<IReceiptReader>(new ScriptedReader("Milk 2.50"));   // the stand-in, last
});
```

In an ASP.NET Core app, replace it once the app has registered its own:

```csharp
await using var bot = await TelegramTestHost.ForLongPollingAsync<Program>(web =>
    web.ConfigureTestServices(services =>
        services.Replace(ServiceDescriptor.Singleton<IReceiptReader>(new ScriptedReader("Milk 2.50")))));
```

**Logs** reach a provider added with `builder.Logging.AddProvider(...)` in the builder overload, or with
`web.ConfigureLogging(logging => logging.AddProvider(...))` in an ASP.NET Core app.

### Chat with it

```csharp
var nick = bot.PrivateChat("Nick");
var anna = bot.GroupChat("Family").Member("Anna");

await nick.SendsAsync("/coffee");

nick.LastMessage.Text.Should().Be("What size?");
nick.LastMessage.Buttons.Should().Equal("Small", "Medium", "Large", "Cancel");
nick.Messages.Select(x => x.ToString())
    .Should().Equal("Nick: /coffee", "Bot: What size? [Small] [Medium] [Large] [Cancel]");
```

Each action returns once the bot is done. Chats show both sides, with edits applied and deletions gone. A name is one
user everywhere.

An action waits up to `bot.UpdateTimeout` (30 s, no limit under a debugger), then says whether the bot never fetched
the update or is stuck in a command. A bot that long-polls, on a generic host or in an ASP.NET Core app, fails the
action at once with the cause if its host stops or one of its background services fails; webhook delivery has no such
check, as a stopped app refuses the post anyway.

An error belongs to the action that caused it: each action rethrows the first error its own update raised, even when
users act at once, and the app's own `ITelegramErrorHandler` still runs, so what it does (an apology to the user, say)
can be checked. If that handler throws too, the action throws an `AggregateException` of the error and then the
handler's failure. A failed poll is rethrown by the next action; an update the test stopped waiting for fails nothing.

### Tap buttons

```csharp
await nick.SendsAsync("/coffee");
var first = nick.LastMessage;
await nick.SendsAsync("/coffee");

var answer = await nick.TapsAsync("Medium");            // on the newest message showing it
var stale = await nick.TapsAsync("Large", on: first);   // on a given message, as it now stands

answer.IsAnswered.Should().BeTrue();
stale.ToString().Should().Be("Notification: That button is no longer active.");
```

An answer reads as `Notification: …`, `Alert: …`, `Answered silently` or `No answer`.

Tapping a button nobody sees fails the test and lists the buttons that are there.

### Send and read files

```csharp
await nick.SendsPhotoAsync(bytes, caption: "Lunch");
await nick.SendsVoiceAsync(bytes, TimeSpan.FromSeconds(3));
await nick.SendsDocumentAsync(bytes, "report.csv");

var report = nick.LastMessage.Document!;   // also .Photo and .Voice
report.FileName.Should().Be("report.csv");
report.ReadAsString().Should().Be("a,b");
```

### Check and fail Telegram

| `bot.Api` | |
| --- | --- |
| `Calls` | Every Bot API call with its parameters. |
| `CommandMenu(scope?)` | The published command menu; the default scope when none is given. |
| `WebhookUrl` | The webhook the bot set. |
| `Fail(method, error, times?, chatId?)` | Makes Telegram refuse a method, for every chat or only one. |

```csharp
bot.Api.Fail("sendMessage", BotApiError.BotBlocked);                                 // every call
bot.Api.Fail("sendPhoto", BotApiError.TooManyRequests(1), times: 1);                 // only the next one
bot.Api.Fail("sendMessage", BotApiError.BotBlocked, chatId: anna.Chat.Id);           // Anna blocked the bot

bot.Api.CommandMenu(new BotCommandScopeAllPrivateChats()).Should().NotBeEmpty();     // a menu for private chats

await bot.SendAsync(new Update { /* … */ });   // any raw update
```

The bot can write only to a chat Telegram knows: open it first with `PrivateChat` or `GroupChat` (a raw update's chat
counts too), also for messages the bot starts itself.

To fail a call made during startup, arrange the fake first:

```csharp
var api = new FakeBotApi();
api.Fail("setMyCommands", new BotApiError(500, "Internal Server Error"));

await using var bot = await TelegramTestHost.ForLongPollingAsync(
    services =>
        services.AddTelegramLongPollingReceiving(receiver => receiver.Token = "unused", typeof(StartCommand).Assembly),
    api
);
```

A method's first matching failure applies until its `times` run out, then the next one does. The fake answers like
Telegram, with Telegram's own error texts, and fails the test on a method it doesn't support. It enforces:

- `allowed_updates`: an action whose type the bot didn't ask for fails before anything changes;
- limits: text up to 4096 characters, captions up to 1024, answers up to 200, callback data of 1-64 bytes, and inline
  buttons that each do something;
- trimming of the text and captions the bot sends, entities included;
- only chats Telegram knows;
- one answer per tap;
- "message is not modified";
- edits only of the bot's own messages, text edits only of text and caption edits only of files, and edits and
  deletions only of messages still there;
- downloads up to 20 MB.

## Samples

- [`Sandbox`](src/Bladehero.Telegram.Platform.Sandbox): long polling with a `/coffee` [conversation](#conversations)
  covering text and button steps, cancelling and stale buttons, and a logger of `MyChatMember` updates.
- [`Sandbox.Webhook`](src/Bladehero.Telegram.Platform.Sandbox.Webhook): an ASP.NET Core echo with an Again button. It
  receives by webhook when `Telegram:BaseUrl` is set and by long polling otherwise, and its scenarios are tested in both
  modes.

Their component tests live in `tests/`. To run a sample against Telegram:

```sh
cd src/Bladehero.Telegram.Platform.Sandbox
dotnet user-secrets set "TelegramReceiverConfiguration:Token" "123456:ABC-DEF..."
dotnet run
```

`Sandbox.Webhook` reads `Telegram:Token`, and for a webhook also `Telegram:BaseUrl` and `Telegram:UpdateEndpoint`.

## License

[MIT](LICENSE)
