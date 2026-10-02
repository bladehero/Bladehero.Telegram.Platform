using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.History.InMemory.Tests;

public sealed class InMemoryHistoryDependencyInjectionTests
{
    private const long Group = -1001234567890;

    [Fact]
    public void UseInMemory_WithACapBelowOne_ShouldThrow()
    {
        // Arrange
        var history = new ServiceCollection().AddTelegramHistory();

        // Act
        var act = () => history.UseInMemory(maxEntriesPerChat: 0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxEntriesPerChat");
    }

    [Fact]
    public void UseInMemory_ShouldGiveEachContainerItsOwnStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTelegramHistory().UseInMemory();

        // Act
        using var first = services.BuildServiceProvider();
        using var second = services.BuildServiceProvider();

        // Assert
        first
            .GetRequiredService<ITelegramHistoryStore>()
            .Should()
            .NotBeSameAs(second.GetRequiredService<ITelegramHistoryStore>());
    }

    [Fact]
    public async Task UseInMemory_ShouldKeepWhatTheBotSends()
    {
        // Arrange
        using var host = await StartAsync(history => history.UseInMemory());

        // Act
        await host.Services.GetRequiredService<ITelegramMessages>().SendAsync(Group, "Hi");

        // Assert
        (await host.Services.GetRequiredService<ITelegramHistory>().ReadAsync(new() { ChatId = Group }))
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    Kind = "sendMessage",
                    MessageId = 1,
                    Text = "Hi",
                }
            );
    }

    [Fact]
    public async Task UseInMemory_Twice_ShouldKeepOneStore()
    {
        // Arrange
        using var host = await StartAsync(history => history.UseInMemory(maxEntriesPerChat: 1).UseInMemory(2));
        var messages = host.Services.GetRequiredService<ITelegramMessages>();

        // Act
        await messages.SendAsync(Group, "Hi");
        await messages.SendAsync(Group, "Bye");

        // Assert
        (await host.Services.GetRequiredService<ITelegramHistory>().ReadAsync(new() { ChatId = Group }))
            .Select(x => x.Text)
            .Should()
            .Equal("Hi", "Bye");
    }

    // A bot whose client talks to a canned Telegram, with its history kept as configured.
    private static async Task<IHost> StartAsync(Action<TelegramHistoryBuilder> store)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Services.AddTelegramBot(
            bot => bot.Token = "1234567:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw",
            httpClientFactory: _ => new HttpClient(new CannedTelegram())
        );
        store(builder.Services.AddTelegramHistory());
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    // Answers every sendMessage with the message sent, numbered from 1.
    private sealed class CannedTelegram : HttpMessageHandler
    {
        private int _sent;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
            var answer = new
            {
                ok = true,
                result = new
                {
                    message_id = Interlocked.Increment(ref _sent),
                    date = 1790000000,
                    text = sent.GetProperty("text").GetString(),
                    chat = new
                    {
                        id = Group,
                        type = "supergroup",
                        title = "Coffee lovers",
                    },
                },
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json"),
            };
        }
    }
}
