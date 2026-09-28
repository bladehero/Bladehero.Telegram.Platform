using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
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
    public static Task<TelegramTestHost> StartAsync() =>
        TelegramTestHost.ForLongPollingAsync(services =>
            services.AddTelegramLongPollingReceiving(receiver => receiver.Token = "unused", typeof(TestBot).Assembly)
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

    // Deletes the command itself, as a bot keeping its chat tidy would.
    private sealed class TidyCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/tidy"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.DeleteMessage(request.Payload.Chat, request.Payload.Id, cancellationToken: token);
    }

    private sealed class MenuCommand : MessageCommand
    {
        private static readonly InlineKeyboardMarkup Menu = new([
            [
                InlineKeyboardButton.WithCallbackData("A", "pick:A"),
                InlineKeyboardButton.WithCallbackData("B", "pick:B"),
            ],
            [InlineKeyboardButton.WithCallbackData("Dismiss", "dismiss")],
            [InlineKeyboardButton.WithUrl("Docs", "https://example.com")],
        ]);

        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/menu"));

        protected override Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            request.Client.SendMessage(request.Payload.Chat, "Pick one", replyMarkup: Menu, cancellationToken: token);
    }

    // Edits the menu into the pick, which takes its keyboard away.
    private sealed class PickCommand : CallbackQueryCommand
    {
        protected override Task<bool> CanHandleAsync(
            TypedCommandRequest<CallbackQuery> request,
            CancellationToken token
        ) => Task.FromResult(request.Payload.Data?.StartsWith("pick:") is true);

        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            var (_, query, client) = request;

            await client.AnswerCallbackQuery(query.Id, cancellationToken: token);
            await client.EditMessageText(
                query.Message!.Chat,
                query.Message.Id,
                $"{query.From.FirstName} picked {query.Data!["pick:".Length..]}",
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
