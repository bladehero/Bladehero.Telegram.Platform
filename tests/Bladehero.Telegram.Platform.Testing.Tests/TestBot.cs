using System.Text;
using Bladehero.Telegram.Platform.Receiving.Background;
using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Microsoft.Extensions.DependencyInjection;
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
                services?.Invoke(collection);
            },
            api
        );

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
