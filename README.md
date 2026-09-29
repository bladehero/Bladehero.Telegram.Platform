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
  [chat](#chat-with-it) · [taps](#tap-buttons) · [files](#send-and-read-files) · [waits](#wait-for-later-messages) ·
  [fake Telegram](#check-and-fail-telegram) · [production apps](#test-a-production-app)
- [Samples](#samples)
- [Upgrading](#upgrading)

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
    "UpdateEndpoint": "telegram/updates",
    "SecretToken": "a-long-random-string"
  }
}
```

Startup calls `setWebhook` only when the URL or settings changed, or on every start with a secret token, which
Telegram never shows back. Keep the tokens in user secrets or environment variables.

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
| `AllowedUpdates` | both | `UpdateType`s to receive. Unset asks for Telegram's default set (all but `ChatMember` and reactions) explicitly, so a list an earlier deployment set doesn't linger. |
| `DropPendingUpdates` | both | Discard updates queued while the bot was down. |
| `SyncCommandMenu` | both | Publish the [command menu](#command-menu) on startup. Default `true`. |
| `CommandMenuScope` | both | `Default`, `AllPrivateChats`, `AllGroupChats` or `AllChatAdministrators`. |
| `Offset`, `Limit` | polling | Update id to resume from; max updates per poll. |
| `BaseUrl`, `UpdateEndpoint` | webhook | Public origin and endpoint path. **Required.** |
| `SecretToken` | webhook | Optional, recommended: 1-256 characters of `A-Z a-z 0-9 _ -`. Sent to Telegram with the webhook; other requests to the endpoint get 401. |

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

`IsCommand` doesn't check the bot's own username: `/last@AnyBot` counts too, which matters in a group with several
bots.

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

`KnownUserCommand<TUser>` runs only for users your `ITelegramUserResolver<TUser>` recognises; strangers are ignored.

```csharp
services.AddScoped<ITelegramUserResolver<User>, UserResolver>();

internal sealed class UserResolver(IUsers users) : ITelegramUserResolver<User>
{
    public Task<User?> ResolveAsync(long chatId, long userId, CancellationToken token) =>
        users.FindByTelegramIdAsync(userId, token);
}

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

The resolver gets the chat an update came from and the user who sent it, the same id in a private chat: resolve by
`userId` to know a person in every chat, or by `chatId` to know a chat, such as a household's group. A message without
a sender, or one sent on behalf of a chat (a channel's post in a group, or an anonymous admin), whose sender is only a
placeholder, is declined unresolved.

Startup fails when a known-user command's `ITelegramUserResolver<TUser>` isn't registered, naming the commands.

### Buttons with typed data

Declare a button's data as a record struct, build keyboards from it, and handle it with
`CallbackQueryCommand<TData>`, or `KnownUserCallbackQueryCommand<TUser, TData>` to have the tapper resolved too:

```csharp
[Button("redeem")]
internal readonly record struct Redeem(long OwnerId, int Points);

var card = new InlineKeyboardMarkup()
    .AddButton("Redeem 10", new Redeem(member.UserId, 10))   // redeem:7000000001:10
    .AddButton("Redeem 50", new Redeem(member.UserId, 50));

internal sealed class RedeemButton(MemberDirectory members) : KnownUserCallbackQueryCommand<Member, Redeem>
{
    protected override Task<ButtonCheck> CheckAsync(
        TypedCommandRequest<CallbackQuery> request,
        CancellationToken token
    ) =>
        Task.FromResult(
            Parsed.OwnerId == User.UserId ? ButtonCheck.Accept : ButtonCheck.Reject("Not your card.", showAlert: true)
        );

    protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
    {
        var (ownerId, points) = Parsed;
        members.Spend(ownerId, points);
        await request.Client.AnswerCallbackQuery(request.Payload.Id, $"Redeemed {points}", cancellationToken: token);
    }
}
```

The data is the prefix, then `:` and each field of the constructor, in the invariant culture:

| Field | Written as |
| --- | --- |
| integers | `-1001234567890` |
| `bool` | `1` (or `0`) |
| `Guid` | `0199c3…`, 32 lower-case hex digits |
| enum | `large`, its name in lower case |
| `DateOnly` | `2026-09-29` |
| `string` | `a%3Ab` for `a:b`: `%`, `:` and `@` escaped |
| nullable, `null` | an empty segment: `redeem::10` |

Only this canonical form decodes.

- **Building data:** `keyboard.AddButton(text, button)`, `ButtonData.Button(text, button)` for an
  `InlineKeyboardButton`, and `ButtonData.Encode` and `TryDecode` for the data alone.
- **Size:** Telegram takes 64 bytes of callback data. It's checked when the button is built, with an
  `ArgumentException` giving the data and its size. Test users have Telegram-sized ids, so tests hit the limit where
  production would.
- **Checked when the receiving services are added**, listing every problem: prefixes (1–32 of `a-z0-9_-`, unique);
  field types, and that every settable value is a constructor parameter; one regular command per button type (or one
  per step); and that a command for a type without `[Button]` overrides `Parse`, as hand-written data still can:

```csharp
// In a CallbackQueryCommand<(string Field, int Step)>
protected override (string Field, int Step)? Parse(string data) =>
    data.Split(':') is ["move", var field, var step] && int.TryParse(step, out var by) ? (field, by) : null;
```

**Checks on a tap.** `CheckAsync` returns `Accept`, `Decline` (the tap is another command's) or
`Reject(answer, showAlert)`, which is answered instead of running `HandleAsync`; override `RejectedAsync` to edit or
delete the card as well. It runs alongside other commands' checks, so keep it free of side effects.
`KnownUserCommand<TUser>` has `AcceptsAsync` for the same, once the user is resolved.

**Taps no command takes**, on a registered prefix, are answered: "That button is no longer active." when the data no
longer decodes, and silently otherwise, e.g. for a stranger. Register an `IButtonRefusalHandler`, in any order, to
answer differently. Hand-written data is left alone, and a custom `ITelegramCommandExecutor` does none of this.

**Changing a button.** Buttons already in chats keep their data. A new trailing field with a default keeps them
decoding; any other change makes them "no longer active", unless `Parse` accepts the old form for a while:

```csharp
protected override ExpenseButton? Parse(string data) => base.Parse(data) ?? Legacy(data);
```

Hand-written and typed data live side by side: a prefix matches the whole first segment, so `exp` never claims
`export:1`.

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

For more than text (editing, deleting, sending files), inject the bot's `ITelegramBotClient`: the same client the
library receives and replies with. To build it differently, e.g. for a local Bot API server, register your own
`ITelegramBotClient` as a singleton, before or after these calls, and the library uses that one too:

- Each of the library's services resolves it once and keeps it (the webhook endpoint per request). A scoped
  registration fails when scopes are validated, and a typed `IHttpClientFactory` client is captured once per service
  (set `PooledConnectionLifetime` on its handler if DNS changes matter).
- `httpClientFactory` applies only to the client the library builds; configure your own client's `HttpClient` yourself.
- Register it as `ITelegramBotClient`: one registered only as `TelegramBotClient` leaves the library building a second.
- A client for another bot goes under a key (`AddKeyedSingleton<ITelegramBotClient>("alerts", …)`), or it becomes this
  bot's client.

## Errors and the HttpClient

Receiver errors go to `ITelegramErrorHandler`. The default one logs every error: one from an update at Error, a
timed-out call included, and a failed poll at Warning; shutdown's own cancellation isn't an error and never reaches it.
Each `TelegramError` carries the `Exception` and the `Update` being handled (`null` for a failed poll), so a handler can
tell the user something went wrong. Replace it by registering your own **after** the receiving services:

```csharp
services.AddScoped<ITelegramErrorHandler, SentryTelegramErrorHandler>();
```

Long polling keeps running after any failure: a command that throws, even a stray `OperationCanceledException` such as
an `HttpClient` timeout, a command graph that can't be built, or an error handler that throws itself (that is logged).
After a failed poll, e.g. while Telegram is unreachable, it waits 1 s before polling again, doubling up to 30 s while
polls keep failing. The wait uses the app's `TimeProvider` when one is registered, and the system clock otherwise.

The webhook endpoint answers 200 once handling has started, even if a command or the error handler fails, so Telegram
doesn't deliver the update again. It answers otherwise only with 401 without the secret token, 400 for a body that
isn't an update, or 500 when the update handler can't be built.

`AddTelegramBot` and the `IConfiguration` overloads of the receiving methods take an `httpClientFactory` for proxies,
IPv4, retries or logging. It builds the library's client, so it doesn't apply to a client you register yourself:

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

The bot's `ITelegramBotClient` talks to the fake, and so does an `ITelegramBotClient` or `TelegramBotClient` the app
registers itself. A client registered in DI is swapped, by instance or by factory. One built by hand isn't:
one in `Program`, or one inside another service's constructor or factory (e.g.
`new MyNotifier(new TelegramBotClient(token))`) still talks to Telegram, so give the tests a dummy token.

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

**Seed users** the app must know before it starts with the ids Telegram will give them. The ids are Telegram-sized,
from 7 000 000 001, so use `UserIdOf`, never hard-coded ids:

```csharp
var api = new FakeBotApi();
var nick = api.UserIdOf("Nick");   // the id PrivateChat("Nick") gets later

await using var bot = await TelegramTestHost.ForLongPollingAsync<Program>(
    web => web.UseSetting("Budget:Owners:0", nick.ToString()),
    api);

var chat = bot.PrivateChat("Nick");   // open it before the bot writes to Nick
```

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

| `TestUser` | |
| --- | --- |
| `SendsAsync`, `SendsPhotoAsync`, `SendsVoiceAsync`, `SendsDocumentAsync` | Send; return the message as posted. |
| `SendsAlbumAsync`, `SendsDocumentAlbumAsync` | Send 2 to 10 photos or files as one album. |
| `EditsAsync(message, text)` | Edit the user's own text message. |
| `TapsAsync(text or predicate, on?)`, `TapsAsync<TButton>(which?, on?)` | Tap an inline button; return the bot's answer. |
| `WaitForMessageAsync(match, after?, timeout?)` | Wait for a message the bot sends later; also on `TestChat`. |

A sent message is a snapshot that stays valid even if the bot deletes it. A user can edit their own text message: the
chat shows the edit, and the bot gets an `edited_message`.

```csharp
var order = await nick.SendsAsync("2 coffees");
await nick.EditsAsync(order, "3 coffees");
```

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

Where labels repeat, such as a ◀ and a ▶ for each of two fields, pick the button by its data; tapping a repeated label
by text fails and points there:

```csharp
await nick.TapsAsync(b => b.CallbackData == "date:next");
```

[Typed buttons](#buttons-with-typed-data) are tapped and read by their data:

```csharp
await nick.TapsAsync<Redeem>(x => x.Points == 50);
nick.LastMessage.ButtonsOf<Redeem>().Should().Equal(new Redeem(nick.Id, 10), new Redeem(nick.Id, 50));
```

Tapping a button nobody sees fails the test and lists the buttons that are there.

A tap uses the message as it now stands, so a button the bot has since removed can't be tapped again. A double tap is
two taps at once:

```csharp
var answers = await Task.WhenAll(nick.TapsAsync("Add", on: card), nick.TapsAsync("Add", on: card));
```

To act as a client that hasn't seen an edit yet, send the tap as a raw `callback_query` built from a snapshot of the
message. Its answer isn't returned; read it from `bot.Api.Calls`:

```csharp
var card = nick.LastMessage;   // a snapshot, buttons and all
// … the bot edits the card …

await bot.SendAsync(new Update
{
    CallbackQuery = new CallbackQuery
    {
        Id = "stale-tap",
        From = new User { Id = nick.Id, FirstName = "Nick" },
        Message = card.Message,
        ChatInstance = "1",
        Data = "size:large",
    },
});
var answer = bot.Api.Calls.Last(x => x.Method == "answerCallbackQuery").Parameters["text"];
```

### Send and read files

```csharp
await nick.SendsPhotoAsync(bytes, caption: "Lunch");
await nick.SendsVoiceAsync(bytes, TimeSpan.FromSeconds(3));
await nick.SendsDocumentAsync(bytes, "report.csv");

var report = nick.LastMessage.Document!;   // also .Photo and .Voice
report.FileName.Should().Be("report.csv");
report.ReadAsString().Should().Be("a,b");
```

An album is 2 to 10 photos or files in one media group, each its own message and update, sent once the bot has handled
the one before; its caption goes under the first photo, or under the last file as the apps put it:

```csharp
await nick.SendsAlbumAsync([front, back], caption: "Receipt");
await nick.SendsDocumentAlbumAsync([new(march, "march.csv"), new(scan, "scan", "application/pdf")], caption: "Q2");
```

Each `TestDocument` has its bytes, its name and, when the extension doesn't tell, its MIME type. A webhook bot gets an
album's items one by one in tests too, where real Telegram may post them at once.

### Wait for later messages

A message the bot sends after the update was handled, such as a notification from a background job, is waited for:

```csharp
var import = await nick.SendsAsync("/import");   // answers "Importing…" and imports in the background
var done = await nick.WaitForMessageAsync(x => x.Text?.StartsWith("Imported") is true, after: import);
```

It returns the newest matching message, or else the first to match later, new or edited. Without `after:`, a match
already in the chat, such as an earlier import's notice, is returned at once; with it, only messages newer than the
given one count. It looks again on every change to the chat, without polling, and after `UpdateTimeout` fails showing
the chat.

A message that waits on a timer comes as soon as the test moves the clock on, when the bot takes its time from an
injected `TimeProvider` (`Task.Delay(delay, timeProvider, token)`). Register a `FakeTimeProvider`, from
`Microsoft.Extensions.TimeProvider.Testing`, after the bot's own registrations:

```csharp
var time = new FakeTimeProvider();
await using var bot = await TelegramTestHost.ForLongPollingAsync(services =>
{
    services.AddReminderBot(configuration);          // the app's registrations
    services.AddSingleton<TimeProvider>(time);       // the clock, last
});
var nick = bot.PrivateChat("Nick");
await nick.SendsAsync("/remind 1m");

time.Advance(TimeSpan.FromMinutes(1));
var reminder = await nick.WaitForMessageAsync(x => x.Text?.StartsWith("⏰") is true);
```

A timer must exist before the test moves the clock: start it while the update is handled (as the `Sandbox` barista's
`OrderQueue` does) or at startup (e.g. a periodic scan's `new PeriodicTimer(period, timeProvider)` created when the host
starts). One a background loop creates later may miss the move and wait for the next. Long polling's wait after a
failed poll runs on the same clock, so with a `FakeTimeProvider` it too lasts until the test moves the clock on.

### Check and fail Telegram

| `bot.Api` | |
| --- | --- |
| `Calls` | Every Bot API call with its parameters. |
| `CommandMenu(scope?)` | The published command menu; the default scope when none is given. |
| `WebhookUrl` | The webhook the bot set. |
| `Fail(method, error, times?, chatId?)` | Makes Telegram refuse a method, for every chat or only one. |
| `FailNetwork(method, times?, chatId?)` | Makes a method's calls fail on the network (`RequestException`). |
| `UserIdOf(firstName)` | The Telegram id a test user gets, reserved before the host starts. |

```csharp
bot.Api.Fail("sendMessage", BotApiError.BotBlocked);                                 // every call
bot.Api.Fail("sendPhoto", BotApiError.TooManyRequests(1), times: 1);                 // only the next one
bot.Api.Fail("sendMessage", BotApiError.BotBlocked, chatId: anna.Chat.Id);           // Anna blocked the bot
bot.Api.FailNetwork("sendMessage", times: 1);                                        // the next one never arrives

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

A method's first matching failure, from `Fail` or `FailNetwork`, applies until its `times` run out, then the next one
does. A `chatId` for a method without a chat, such as `answerCallbackQuery`, is refused.

The fake answers like Telegram, with Telegram's own error texts, and supports:

- `getMe`, `getUpdates` and file downloads;
- `sendMessage`, `sendPhoto`, `sendDocument`, `sendVoice`, `sendChatAction`;
- `editMessageText`, `editMessageCaption`, `editMessageReplyMarkup`, `deleteMessage`;
- `answerCallbackQuery`, `getFile`;
- `setWebhook`, `getWebhookInfo`, `deleteWebhook`;
- `getMyCommands`, `setMyCommands`.

Any other method fails the test, naming it. It enforces:

- `allowed_updates`, one list per bot: the types it asked for last, with `getUpdates` or `setWebhook`; an action of
  another type fails before anything changes;
- limits: text up to 4096 characters, captions up to 1024, answers up to 200, callback data of 1-64 bytes, and inline
  buttons that each do something; text is measured raw, so formatted text near a limit may be refused where Telegram,
  counting it without its markup, would take it;
- trimming of the text and captions the bot sends, entities included;
- only chats Telegram knows;
- one answer per tap;
- "message is not modified";
- edits only of the bot's own messages, text edits only of text and caption edits only of files, and edits and
  deletions only of messages still there;
- downloads up to 20 MB.

It keeps text as the bot sent it, without parsing or checking HTML or Markdown, so `parse_mode` changes nothing: assert
on the raw text, and try the markup against real Telegram.

### Test a production app

**Heavy hosted services** the tests don't need, such as a model warm-up, a scanner or a poller, are removed by their
implementation type. Never `RemoveAll<IHostedService>()`: the bot's own polling loop is a hosted service too.

```csharp
services.Remove(services.Single(x =>
    x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(ModelWarmup)));
```

For an internal type you can't name, match on `x.ImplementationType?.Name == "ModelWarmup"`.

**The database:** point the app's connection string at a file of the test's own, so a test never writes to the
developer's real database, and delete it once the host is disposed:

```csharp
var path = Path.Combine(Path.GetTempPath(), $"budget-{Guid.NewGuid():N}.db");
var bot = await TelegramTestHost.ForLongPollingAsync<Program>(web =>
    web.UseSetting("ConnectionStrings:Database", $"Data Source={path};Pooling=False"));   // no pool keeps it open
try
{
    // … the test …
}
finally
{
    await bot.DisposeAsync();
    foreach (var file in new[] { path, path + "-wal", path + "-shm" })
    {
        File.Delete(file);   // no error if it isn't there
    }
}
```

**In-memory SQLite:** each `:memory:` connection opens its own empty database, so contexts in different scopes would
not see each other's data. Share one open connection across the bot's scopes:

```csharp
var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();   // the database lives as long as this connection
services.AddDbContext<BudgetContext>(options => options.UseSqlite(connection));   // after the app's own AddDbContext
```

**Seed data or run an app service** through the bot's own container, in a scope as the app would:

```csharp
await using var scope = bot.Services.CreateAsyncScope();

var budgets = scope.ServiceProvider.GetRequiredService<BudgetContext>();
budgets.Limits.Add(new Limit("Groceries", 300));
await budgets.SaveChangesAsync();

await scope.ServiceProvider.GetRequiredService<MonthlyReport>().SendAsync(CancellationToken.None);
```

**A restart** is a second host on the same `FakeBotApi`, which keeps the chats, their messages and the command menu:

```csharp
var api = new FakeBotApi();
await using (var first = await TelegramTestHost.ForLongPollingAsync(services => services.AddBudgetBot(config), api))
{
    await first.PrivateChat("Nick").SendsAsync("/limit groceries 300");
}

await using var bot = await TelegramTestHost.ForLongPollingAsync(services => services.AddBudgetBot(config), api);
var nick = bot.PrivateChat("Nick");   // the same chat, as the bot left it
```

**One command per button:** Telegram takes one answer per tap, and a second fails with its own "query is too old and
response timeout expired or query ID is invalid". When two commands claim the same button both run, and the second
answer fails the action, so give each button's data to exactly one command. [Typed buttons](#buttons-with-typed-data)
are checked for this at startup.

## Samples

- [`Sandbox`](src/Bladehero.Telegram.Platform.Sandbox): a long-polling coffee shop, composed in one `AddCoffeeShop` that
  Program and the tests share. It shows:
  - `/start` answered by three commands in turn, by [priority](#priorities);
  - the [`[BotCommand]` menu](#command-menu), and a `/help` listing it from `IBotCommandMenu`;
  - a loyalty club of [known users](#known-users), resolved by user id and seeded from `CoffeeShop:Members`: `/join`,
    `/leave`, `/redeem 10` with arguments, and a `/points` card that is edited in place or sent again, with
    [typed](#buttons-with-typed-data) Redeem buttons whose `CheckAsync` refuses anyone but the card's owner;
  - receipts for points: photos, PDFs, and photo and PDF albums read by a stand-in for an AI reader, too-big and
    unsupported files turned down, and `/history` sending a CSV file; the receipt and album buttons are typed, with the
    same owner check;
  - a `/coffee` [conversation](#conversations) bound to its card and its customer, which a voice message can start too,
    through a stand-in for a transcriber, and whose cup name the customer can fix by editing their message;
  - stale, foreign and double-tapped buttons: an earlier order's, another member's card, a receipt taken already, and
    a tap from a view that missed an edit;
  - a barista telling each customer when their coffee is ready, [sent on its own](#sending-on-your-own) through
    `ITelegramSender`, on the clock of an injected `TimeProvider`;
  - an [error handler](#errors-and-the-httpclient) that apologises in the chat, even to someone who blocked the bot;
  - a greeting when the bot is added to a group, next to a logger of `MyChatMember` updates;
  - component tests of restarts on the same fake with and without a shared conversation store, members seeded with
    `UserIdOf` before the host starts, Telegram refusing calls to one chat or asking the bot to slow down, and a
    `FakeTimeProvider` moving the barista's clock on. The stand-ins are registered after `AddCoffeeShop`, in place of
    its disabled defaults.
- [`Sandbox.Webhook`](src/Bladehero.Telegram.Platform.Sandbox.Webhook): an ASP.NET Core app that receives by webhook
  when `Telegram:BaseUrl` is set and by long polling otherwise, with every scenario tested in both modes. It shows:
  - an echo of plain text with a [typed](#buttons-with-typed-data) Louder button next to an Again button with
    hand-written data;
  - `/translate` through an `ITranslator` that the tests [replace](#configure-the-app-under-test) with
    `ConfigureTestServices`;
  - a photo sent back by its file id, without uploading it again;
  - a `/remember` [conversation](#conversations), with `/recall`;
  - the [command menu](#command-menu), and a webhook guarded by its secret token.

Their component tests live in `tests/`. To run a sample against Telegram:

```sh
cd src/Bladehero.Telegram.Platform.Sandbox
dotnet user-secrets set "TelegramReceiverConfiguration:Token" "123456:ABC-DEF..."
dotnet run
```

`Sandbox.Webhook` reads `Telegram:Token`, and for a webhook also `Telegram:BaseUrl`, `Telegram:UpdateEndpoint` and,
optionally, `Telegram:SecretToken`.

## Upgrading

### From 10.0.x

- `ITelegramUserResolver<TUser>.ResolveAsync(chatId, token)` is now `ResolveAsync(chatId, userId, token)`. Resolve by
  `chatId` to keep the old behaviour.
- Known-user commands get their resolver from the container:
  `class X(ITelegramUserResolver<User> users) : KnownUserCommand<User>(users)` becomes
  `class X : KnownUserCommand<User>`. Test them through `TelegramTestHost` rather than building them by hand.
- A hand-written base that parses callback data and resolves the user, such as a
  `ParsedCallbackQueryCommand<TUser, TParsed>`, becomes `KnownUserCallbackQueryCommand<TUser, TData>` overriding
  `Parse`, or a `[Button]` type with no `Parse` at all. Checks that need the user go in `CheckAsync`.
- Behaviour since 10.0.x:
  - `IsCommand` ends a command at any whitespace.
  - An unset `AllowedUpdates` asks for Telegram's default explicitly.
  - The default error handler logs every error.
  - The webhook answers 200 once handling has started.
  - An `ITelegramBotClient` the app registers is used.

### To 10.2

- `CallbackQueryCommand<TData>.Parse` is no longer abstract. Overrides keep working; a command for a type without
  `[Button]` that forgets it now fails at startup, not at compile time.
- New hooks: `CheckAsync`, `RejectedAsync` and `AcceptsAsync`. An existing method with the same signature gets warning
  CS0114; rename it or make it an override.
- Startup fails when a known-user command's resolver isn't registered. Before, every update failed.
- Taps on typed buttons that no command takes are answered. Hand-written data is untouched.
- Test users have Telegram-sized ids.

## License

[MIT](LICENSE)
