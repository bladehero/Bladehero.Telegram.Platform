using System.Text;
using Bladehero.Telegram.Platform.Receiving.Background;
using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.ChatMembers;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.EditedMessages;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

// The one bot all tests run (commands are found by assembly scan). Each command answers only its own trigger; the echo
// answers any non-command text.
internal static class TestBot
{
    // A test's own registrations come after the bot's, so they win.
    public static Task<TelegramTestHost> StartAsync(
        FakeBotApi? api = null,
        Action<IServiceCollection>? services = null,
        Action<TelegramReceiverConfiguration>? receiver = null
    ) =>
        TelegramTestHost.ForLongPollingAsync(
            collection =>
            {
                collection.AddTelegramLongPollingReceiving(
                    configuration =>
                    {
                        configuration.Token = "unused";
                        receiver?.Invoke(configuration);
                    },
                    typeof(TestBot).Assembly
                );
                collection.AddSingleton<SeenTaps>();
                services?.Invoke(collection);
            },
            api
        );

    // How many cups: the data of the buttons /cups shows.
    [ButtonData("t-cups")]
    internal readonly record struct Cups(int Count);

    // A cup size in ml, the data of the buttons /size shows; Version counts the card's redraws.
    [ButtonData("t-size")]
    internal readonly record struct Size(int Ml, int Version);

    // The size taps the bot got, as it saw them.
    internal sealed class SeenTaps
    {
        private readonly List<CallbackQuery> _taps = [];

        public CallbackQuery Last
        {
            get
            {
                lock (_taps)
                {
                    return _taps[^1];
                }
            }
        }

        public void Add(CallbackQuery tap)
        {
            lock (_taps)
            {
                _taps.Add(tap);
            }
        }
    }

    // "Pick a size" with Small, Large and a Remove that deletes the card.
    private sealed class SizeCardCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/size"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(
                request.Payload.Chat,
                "Pick a size",
                replyMarkup: SizeKeyboard(version: 1),
                cancellationToken: token
            );
    }

    // Answers "Size 400" and redraws the card one version on; a tap on a deleted card is only answered.
    private sealed class SizeCommand(SeenTaps? seen = null) : CallbackQueryCommand<Size>
    {
        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            var (_, query, client) = request;
            seen?.Add(query);

            if (query.Message is not { Date.Year: > 1970 } card)
            {
                await client.AnswerCallbackQuery(query.Id, "That card is gone", cancellationToken: token);
                return;
            }

            await client.AnswerCallbackQuery(query.Id, $"Size {Parsed.Ml}", cancellationToken: token);
            await client.EditMessageText(
                card.Chat,
                card.Id,
                $"Size {Parsed.Ml}",
                replyMarkup: SizeKeyboard(Parsed.Version + 1),
                cancellationToken: token
            );
        }
    }

    private static InlineKeyboardMarkup SizeKeyboard(int version) =>
        new InlineKeyboardMarkup()
            .AddButton("Small", new Size(250, version))
            .AddButton("Large", new Size(400, version))
            .AddButton("Remove", "dismiss");

    // "Step 1" with Next, which edits the text and then the buttons, one call each.
    private sealed class StepsCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/steps"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(
                request.Payload.Chat,
                "Step 1",
                replyMarkup: InlineKeyboardButton.WithCallbackData("Next", "steps-next"),
                cancellationToken: token
            );
    }

    private sealed class NextStepCommand : CallbackQueryCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.Data == "steps-next");

        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            var (_, query, client) = request;
            var card = query.Message!;

            await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
            await client.EditMessageText(
                card.Chat,
                card.Id,
                "Step 2",
                replyMarkup: InlineKeyboardButton.WithCallbackData("Next", "steps-next"),
                cancellationToken: token
            );
            await client.EditMessageReplyMarkup(
                card.Chat,
                card.Id,
                InlineKeyboardButton.WithCallbackData("Done", "steps-done"),
                cancellationToken: token
            );
        }
    }

    // Greets whoever joins a group.
    private sealed class WelcomeCommand : ChatMemberCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<ChatMemberUpdated> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.NewChatMember is ChatMemberMember);

        protected override Task HandleAsync(TypedCommandRequest<ChatMemberUpdated> request, CancellationToken token) =>
            request.Client.SendMessage(
                request.Payload.Chat,
                $"Welcome, {request.Payload.NewChatMember.User.FirstName}",
                cancellationToken: token
            );
    }

    // Never finishes, and ignores cancellation too.
    private sealed class HangCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/hang"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            new TaskCompletionSource().Task;
    }

    // Finishes only when the bot stops.
    private sealed class WaitCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/wait"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.Delay(Timeout.InfiniteTimeSpan, token);
    }

    private sealed class EchoCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.Text?.StartsWith('/') is false);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(request.Payload.Chat, request.Payload.Text!, cancellationToken: token);
    }

    private sealed class SlowCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/slow"));

        protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), token);
            await request.Client.SendMessage(request.Payload.Chat, "done", cancellationToken: token);
        }
    }

    private sealed class BoomCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/boom"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            throw new InvalidOperationException($"boom from {request.Payload.From!.FirstName}");
    }

    private sealed class SlowBoomCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/slowboom"));

        protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken.None);
            throw new InvalidOperationException("slow boom");
        }
    }

    // Fails as a timed-out HTTP call does: with a cancellation nobody asked for.
    private sealed class TimeoutCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/timeout"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            throw new TaskCanceledException("Claude timed out");
    }

    // "/probe Error Critical" logs one entry per level, with an exception from Error up; "/probe" logs at Debug.
    private sealed class ProbeCommand(ILogger<ProbeCommand> logger) : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.Text?.Split(' ') is ["/probe", ..]);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            var levels = request.Payload.Text!.Split(' ')[1..] is { Length: > 0 } names ? names : ["Debug"];
            foreach (var level in levels.Select(Enum.Parse<LogLevel>))
            {
                var failure = level >= LogLevel.Error ? new InvalidOperationException("The ledger is off") : null;
                logger.Log(level, failure, "Probed by {Name}", request.Payload.From!.FirstName);
            }

            return Task.CompletedTask;
        }
    }

    // Lets "/latefail" log its error once Go is set; Done is set once it has.
    internal sealed class LateWork
    {
        public TaskCompletionSource Go { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // "/latefail" starts work it doesn't wait for, which logs an error; optional, so hosts without LateWork still start.
    private sealed class LateFailureCommand(ILogger<LateFailureCommand> logger, LateWork? late = null) : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(late is not null && request.Payload.IsCommand("/latefail"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            var name = request.Payload.From!.FirstName;
            _ = Task.Run(
                async () =>
                {
                    await late!.Go.Task;
                    logger.LogError("Late failure for {Name}", name);
                    late.Done.TrySetResult();
                },
                CancellationToken.None
            );

            return Task.CompletedTask;
        }
    }

    // Replies with the sender's details as the bot sees them, and the chat's username.
    private sealed class DetailsCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/details"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            var (_, message, client) = request;
            var from = message.From!;

            return client.SendMessage(
                message.Chat,
                $"{from.FirstName} {from.LastName} @{from.Username} {from.LanguageCode}; chat @{message.Chat.Username}",
                cancellationToken: token
            );
        }
    }

    [BotCommand("whoami", "Say who you are")]
    private sealed class WhoAmICommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/whoami"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(
                request.Payload.Chat,
                $"You are {request.Payload.From!.FirstName}",
                cancellationToken: token
            );
    }

    // Downloads any file sent and replies with its content as text.
    private sealed class FileCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload is { Photo: [_, ..] } or { Voice: not null } or { Document: not null });

        protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            var (_, message, client) = request;
            (FileBase File, string What) received = message switch
            {
                { Photo: [.., var largest] } => (largest, "a photo"),
                { Voice: { } voice } => (voice, $"{voice.Duration}s of voice"),
                _ => (message.Document!, $"{message.Document!.FileName} as {message.Document.MimeType}"),
            };

            using var content = new MemoryStream();
            await client.GetInfoAndDownloadFile(received.File, content, token);
            await client.SendMessage(
                message.Chat,
                $"Got {received.What}: {Encoding.UTF8.GetString(content.ToArray())}",
                cancellationToken: token
            );
        }
    }

    private sealed class PhotoCommand : MessageCommand
    {
        private static readonly InlineKeyboardMarkup Buttons = new([
            [
                InlineKeyboardButton.WithCallbackData("Rename", "rename"),
                InlineKeyboardButton.WithCallbackData("Again", "again"),
            ],
        ]);

        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/photo"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendPhoto(
                request.Payload.Chat,
                InputFile.FromStream(new MemoryStream("jpeg bytes"u8.ToArray()), "cat.jpg"),
                caption: "A cat",
                replyMarkup: Buttons,
                cancellationToken: token
            );
    }

    private sealed class RenameCommand : CallbackQueryCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.Data == "rename");

        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            var (_, query, client) = request;

            await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
            await client.EditMessageCaption(
                query.Message!.Chat,
                query.Message.Id,
                "A renamed cat",
                cancellationToken: token
            );
        }
    }

    private sealed class AgainCommand : CallbackQueryCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.Data == "again");

        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            var (_, query, client) = request;

            await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
            await client.SendPhoto(
                query.Message!.Chat,
                InputFile.FromFileId(query.Message.Photo![^1].FileId),
                cancellationToken: token
            );
        }
    }

    private sealed class ReportCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/report"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendDocument(
                request.Payload.Chat,
                InputFile.FromStream(new MemoryStream("a,b\n1,2"u8.ToArray()), "report.csv"),
                caption: "Your report",
                cancellationToken: token
            );
    }

    private sealed class SayCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/say"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendVoice(
                request.Payload.Chat,
                InputFile.FromStream(new MemoryStream("ogg bytes"u8.ToArray()), "hello.ogg"),
                duration: 2,
                cancellationToken: token
            );
    }

    private sealed class CatCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/cat"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendPhoto(
                request.Payload.Chat,
                InputFile.FromUri("https://example.com/cat.jpg"),
                cancellationToken: token
            );
    }

    private sealed class TidyCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/tidy"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.DeleteMessage(request.Payload.Chat, request.Payload.Id, cancellationToken: token);
    }

    // No command handles Ignore, so its tap goes unanswered.
    [BotCommand("menu", "Show the menu")]
    private sealed class MenuCommand : MessageCommand
    {
        private static readonly InlineKeyboardMarkup Menu = new([
            [
                InlineKeyboardButton.WithCallbackData("A", "pick:A"),
                InlineKeyboardButton.WithCallbackData("B", "pick:B"),
            ],
            [
                InlineKeyboardButton.WithCallbackData("Dismiss", "dismiss"),
                InlineKeyboardButton.WithCallbackData("Ignore", "ignore"),
            ],
            [InlineKeyboardButton.WithUrl("Docs", "https://example.com")],
        ]);

        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/menu"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(request.Payload.Chat, "Pick one", replyMarkup: Menu, cancellationToken: token);
    }

    // Answers later, from the background, as a job would after the update was handled.
    private sealed class LaterCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/later"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            var (_, message, client) = request;
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                await client.SendMessage(message.Chat, "later");
            });

            return Task.CompletedTask;
        }
    }

    // Answers a user's edit.
    private sealed class EditedCommand : EditedMessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.Text is not null);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(
                request.Payload.Chat,
                $"You changed it to: {request.Payload.Text}",
                cancellationToken: token
            );
    }

    // Answers each album item with its caption, and cannot read broken.csv.
    private sealed class AlbumCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.MediaGroupId is not null);

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Payload.Document?.FileName == "broken.csv"
                ? throw new InvalidOperationException("Cannot read broken.csv")
                : request.Client.SendMessage(
                    request.Payload.Chat,
                    $"Album item: {request.Payload.Caption ?? "no caption"}",
                    cancellationToken: token
                );
    }

    // Two ◀ and two ▶, told apart only by their data.
    private sealed class CardCommand : MessageCommand
    {
        private static readonly InlineKeyboardMarkup Arrows = new([
            [
                InlineKeyboardButton.WithCallbackData("◀", "method:back"),
                InlineKeyboardButton.WithCallbackData("▶", "method:next"),
            ],
            [
                InlineKeyboardButton.WithCallbackData("◀", "date:back"),
                InlineKeyboardButton.WithCallbackData("▶", "date:next"),
            ],
        ]);

        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/card"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(request.Payload.Chat, "Card", replyMarkup: Arrows, cancellationToken: token);
    }

    // Answers "moved method back" and the like.
    private sealed class ArrowCommand : CallbackQueryCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.Data?.Split(':') is ["method" or "date", "back" or "next"]);

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
            request.Client.AnswerCallbackQuery(
                request.Payload.Id,
                $"moved {request.Payload.Data!.Replace(':', ' ')}",
                cancellationToken: token
            );
    }

    // Answers A with a notification and B with an alert.
    private sealed class PickCommand : CallbackQueryCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.Data?.StartsWith("pick:") is true);

        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            var (_, query, client) = request;
            var pick = query.Data!["pick:".Length..];

            await client.AnswerCallbackQuery(
                query.Id,
                $"You picked {pick}",
                showAlert: pick == "B",
                cancellationToken: token
            );
            await client.EditMessageText(
                query.Message!.Chat,
                query.Message.Id,
                $"{query.From.FirstName} picked {pick}",
                cancellationToken: token
            );
        }
    }

    // Two typed Cups buttons, and a hand-written one beside them.
    private sealed class CupsMenuCommand : MessageCommand
    {
        private static readonly InlineKeyboardMarkup Menu = new InlineKeyboardMarkup()
            .AddButton("1 cup", new Cups(1))
            .AddButton("2 cups", new Cups(2))
            .AddButton("Other", "cups-other");

        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/cups"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(
                request.Payload.Chat,
                "How many cups?",
                replyMarkup: Menu,
                cancellationToken: token
            );
    }

    // Answers "Cups 2" and the like.
    private sealed class CupsCommand : CallbackQueryCommand<Cups>
    {
        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
            request.Client.AnswerCallbackQuery(request.Payload.Id, $"Cups {Parsed.Count}", cancellationToken: token);
    }

    private sealed class DismissCommand : CallbackQueryCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.Data == "dismiss");

        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            var (_, query, client) = request;

            await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
            await client.DeleteMessage(query.Message!.Chat, query.Message.Id, cancellationToken: token);
        }
    }
}
