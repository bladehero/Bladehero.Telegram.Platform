using System.Text;
using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.CommandMenu;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Testing.Tests;

// The bot every test here runs: commands are found by scanning this assembly, so together they make up one bot. Each
// answers only its own command or button — apart from the echo, which answers any text that is not a command.
internal static class TestBot
{
    public static Task<TelegramTestHost> StartAsync(FakeBotApi? api = null) =>
        TelegramTestHost.ForLongPollingAsync(
            services =>
                services.AddTelegramLongPollingReceiving(
                    receiver => receiver.Token = "unused",
                    typeof(TestBot).Assembly
                ),
            api
        );

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
            throw new InvalidOperationException("boom");
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

    // Downloads whatever file a user sends and says what it got, reading the content as text.
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

    // Uploads a photo whose buttons rename it or send it again by its file id.
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

    // Deletes the command itself, as a bot keeping its chat tidy would.
    private sealed class TidyCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/tidy"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.DeleteMessage(request.Payload.Chat, request.Payload.Id, cancellationToken: token);
    }

    // Ignore is a button no command handles, so its tap goes unanswered.
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

    // Edits the menu into the pick, which takes its keyboard away. B is answered with an alert, A with a notification.
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
