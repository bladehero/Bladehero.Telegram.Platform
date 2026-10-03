using Bladehero.Telegram.Platform.Receiving.Background.LongPolling;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Bladehero.Telegram.Platform.Conversations.EntityFrameworkCore.Tests;

// The bot stops and starts again on the same fake and the same database, as a deployment would.
public sealed class ConversationRestartTests
{
    private const string Flow = "expense";
    private const string Step = "category";

    [Fact]
    public async Task Conversation_AfterARestart_ShouldContinueWithItsBoundButtons()
    {
        // Arrange
        await using var database = await TestDatabase.CreateAsync();
        var api = new FakeBotApi();
        await using (var before = await StartAsync(api, database))
        {
            await before.PrivateChat("Nick").SendsAsync("/start");
        }

        await using var bot = await StartAsync(api, database);
        var nick = bot.PrivateChat("Nick");
        var stored = (await RowsAsync(database)).Single();

        // Act
        var answer = await nick.TapsAsync<PickCategory>();

        // Assert
        using (new AssertionScope())
        {
            (stored.ChatId, stored.UserId, stored.Flow, stored.Step).Should().Be((nick.Chat.Id, nick.Id, Flow, Step));
            stored.RunId.Should().NotBeNullOrEmpty();
            answer.ToString().Should().Be("Notification: Groceries it is.");
            (await RowsAsync(database)).Should().BeEmpty("the step ended the conversation");
        }
    }

    private static Task<TelegramTestHost> StartAsync(FakeBotApi api, TestDatabase database) =>
        TelegramTestHost.ForLongPollingAsync(
            (IServiceCollection services) =>
            {
                services.AddTelegramLongPollingReceiving(
                    receiver => receiver.Token = "unused",
                    typeof(ConversationRestartTests).Assembly
                );
                database.AddTo(services);
                services.AddTelegramConversations().UseEntityFrameworkCore<BudgetContext>();
            },
            api
        );

    private static async Task<List<StoredConversation>> RowsAsync(TestDatabase database)
    {
        await using var context = database.NewContext();
        return await context.Set<StoredConversation>().AsNoTracking().ToListAsync();
    }

    // "/start" asks for a category, on a button bound to this run of the flow.
    private sealed class StartCommand(IConversation conversation) : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(request.Payload.IsCommand("/start"));

        protected override async Task HandleAsync(TypedCommandRequest<Message> request, CancellationToken token)
        {
            await conversation.StartAsync(Flow, Step, token);
            var binding = await conversation.BindAsync(token);
            await request.Client.SendMessage(
                request.Payload.Chat,
                "Which category?",
                replyMarkup: new InlineKeyboardMarkup().AddButton("Groceries", new PickCategory("Groceries"), binding),
                cancellationToken: token
            );
        }
    }

    [ConversationStep(Flow, Step)]
    private sealed class PickCategoryStep(IConversation conversation) : CallbackQueryCommand<PickCategory>
    {
        protected override async Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token)
        {
            await conversation.EndAsync(token);
            await request.Client.AnswerCallbackQuery(
                request.Payload.Id,
                $"{Parsed.Name} it is.",
                cancellationToken: token
            );
        }
    }

    [ButtonData("category")]
    private readonly record struct PickCategory(string Name);
}
