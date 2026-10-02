using Bladehero.Telegram.Platform.History;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Tests.History;

public sealed class RecordingBotClientTests
{
    private static readonly Message Sent = new()
    {
        Id = 10,
        Date = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc),
        Chat = new Chat { Id = HistoryHost.ChatId, Type = ChatType.Supergroup },
        Text = "Hi",
    };

    [Fact]
    public async Task SendRequest_ShouldReturnTheResultAndRecordTheCall()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var client = Recording(host, Answering(Sent));

        // Act
        var result = await client.SendMessage(HistoryHost.ChatId, "Hi");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSameAs(Sent);
            (await EntriesAsync(host))
                .Should()
                .ContainSingle()
                .Which.Should()
                .BeEquivalentTo(new { Kind = "sendMessage", MessageId = 10 });
        }
    }

    [Fact]
    public async Task SendRequest_ThatFails_ShouldRethrowTheSameExceptionAndRecordIt()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var failure = new ApiRequestException("Forbidden: bot was blocked by the user", 403);
        var inner = new Mock<ITelegramBotClient>();
        inner
            .Setup(x => x.SendRequest(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        var client = Recording(host, inner);

        // Act
        var act = () => client.SendMessage(HistoryHost.ChatId, "Hi");

        // Assert
        using (new AssertionScope())
        {
            (await act.Should().ThrowAsync<ApiRequestException>()).Which.Should().BeSameAs(failure);
            (await EntriesAsync(host)).Should().ContainSingle().Which.ErrorCode.Should().Be(403);
        }
    }

    [Fact]
    public async Task SendRequest_OfGetUpdates_ShouldNotBeRecorded()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var inner = new Mock<ITelegramBotClient>();
        inner.Setup(x => x.SendRequest(It.IsAny<IRequest<Update[]>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var client = Recording(host, inner);

        // Act
        await client.GetUpdates();

        // Assert
        (await EntriesAsync(host))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task SendRequest_WhenTheEntryCannotBeBuilt_ShouldStillReturnAndLogAnError()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var inner = new Mock<ITelegramBotClient>();
        inner.Setup(x => x.SendRequest(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var client = Recording(host, inner);

        // Act
        var result = await client.SendRequest(new UnserializableRequest());

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeTrue();
            (await EntriesAsync(host)).Should().BeEmpty();
            host.Logs.At(LogLevel.Error)
                .Should()
                .Equal("Couldn't record the unserializable call in the Telegram history.");
        }
    }

    [Fact]
    public async Task SendRequest_WithinAnUpdate_ShouldCarryItsId()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var client = Recording(host, Answering(Sent));

        // Act
        using (TelegramHistoryCause.Begin(new Update { Id = 7 }))
        {
            await client.SendMessage(HistoryHost.ChatId, "Hi");
        }

        // Assert
        (await EntriesAsync(host))
            .Should()
            .ContainSingle()
            .Which.UpdateId.Should()
            .Be(7);
    }

    [Fact]
    public async Task SendRequest_FromWorkTheUpdateStartedButDidNotAwait_ShouldCarryItsId()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var client = Recording(host, Answering(Sent));
        var go = new TaskCompletionSource();
        Task started;
        using (TelegramHistoryCause.Begin(new Update { Id = 7 }))
        {
            started = Task.Run(async () =>
            {
                await go.Task;
                await client.SendMessage(HistoryHost.ChatId, "Hi");
            });
        }

        // Act
        go.SetResult();
        await started;

        // Assert
        (await EntriesAsync(host))
            .Should()
            .ContainSingle()
            .Which.UpdateId.Should()
            .Be(7);
    }

    [Fact]
    public async Task SendRequest_OutsideAnyUpdate_ShouldCarryNone()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var client = Recording(host, Answering(Sent));
        using (TelegramHistoryCause.Begin(new Update { Id = 7 })) { }

        // Act
        await client.SendMessage(HistoryHost.ChatId, "Hi");

        // Assert
        (await EntriesAsync(host))
            .Should()
            .ContainSingle()
            .Which.UpdateId.Should()
            .BeNull();
    }

    [Fact]
    public async Task Members_ShouldDelegateUnchanged()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var parser = Mock.Of<IExceptionParser>();
        var inner = new Mock<ITelegramBotClient>();
        inner.SetupGet(x => x.BotId).Returns(1234567);
        inner.SetupGet(x => x.LocalBotServer).Returns(true);
        inner.SetupGet(x => x.Timeout).Returns(TimeSpan.FromSeconds(42));
        inner.SetupGet(x => x.ExceptionsParser).Returns(parser);
        inner.Setup(x => x.TestApi(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var client = Recording(host, inner);
        AsyncEventHandler<ApiRequestEventArgs> onRequest = (_, _, _) => ValueTask.CompletedTask;
        AsyncEventHandler<ApiResponseEventArgs> onResponse = (_, _, _) => ValueTask.CompletedTask;

        // Act
        client.Timeout = TimeSpan.FromSeconds(7);
        client.ExceptionsParser = parser;
        client.OnMakingApiRequest += onRequest;
        client.OnMakingApiRequest -= onRequest;
        client.OnApiResponseReceived += onResponse;
        client.OnApiResponseReceived -= onResponse;
        var tested = await client.TestApi();

        // Assert
        using (new AssertionScope())
        {
            client.BotId.Should().Be(1234567);
            client.LocalBotServer.Should().BeTrue();
            client.Timeout.Should().Be(TimeSpan.FromSeconds(42));
            client.ExceptionsParser.Should().BeSameAs(parser);
            tested.Should().BeTrue();
            inner.VerifySet(x => x.Timeout = TimeSpan.FromSeconds(7));
            inner.VerifySet(x => x.ExceptionsParser = parser);
            inner.VerifyAdd(x => x.OnMakingApiRequest += onRequest);
            inner.VerifyRemove(x => x.OnMakingApiRequest -= onRequest);
            inner.VerifyAdd(x => x.OnApiResponseReceived += onResponse);
            inner.VerifyRemove(x => x.OnApiResponseReceived -= onResponse);
            (await EntriesAsync(host)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task DownloadFile_ShouldNotBeRecorded()
    {
        // Arrange
        await using var host = await HistoryHost.StartAsync();
        var inner = new Mock<ITelegramBotClient>();
        var client = Recording(host, inner);
        var file = new TGFile
        {
            FileId = "BQAD",
            FileUniqueId = "AgADBQAD",
            FilePath = "documents/file_1.csv",
        };

        // Act
        await client.DownloadFile(file.FilePath!, Stream.Null);
        await client.DownloadFile(file, Stream.Null);

        // Assert
        using (new AssertionScope())
        {
            inner.Verify(x => x.DownloadFile(file.FilePath!, Stream.Null, It.IsAny<CancellationToken>()));
            inner.Verify(x => x.DownloadFile(file, Stream.Null, It.IsAny<CancellationToken>()));
            (await EntriesAsync(host)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Dispose_ShouldDisposeAnInnerClientItBuiltButNotOneItWasGiven()
    {
        // Arrange
        var built = Disposable();
        var given = Disposable();
        var withBuilt = ProviderWith(x => x.AddSingleton<ITelegramBotClient>(_ => built.Object));
        var withGiven = ProviderWith(x => x.AddSingleton(given.Object));
        withBuilt.GetRequiredService<ITelegramBotClient>();
        withGiven.GetRequiredService<ITelegramBotClient>();

        // Act
        await withBuilt.DisposeAsync();
        await withGiven.DisposeAsync();

        // Assert
        using (new AssertionScope())
        {
            built.As<IAsyncDisposable>().Verify(x => x.DisposeAsync(), Times.Once);
            built.As<IDisposable>().Verify(x => x.Dispose(), Times.Never);
            given.As<IAsyncDisposable>().Verify(x => x.DisposeAsync(), Times.Never);
            given.As<IDisposable>().Verify(x => x.Dispose(), Times.Never);
        }
    }

    private static RecordingBotClient Recording(HistoryHost host, Mock<ITelegramBotClient> inner) =>
        new(inner.Object, host.Writer);

    // The app's client, as `register` adds it, then the history.
    private static ServiceProvider ProviderWith(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection().AddLogging();
        register(services);
        services.AddTelegramHistory().Services.AddSingleton(Mock.Of<ITelegramHistoryStore>());
        return services.BuildServiceProvider();
    }

    private static Mock<ITelegramBotClient> Answering(Message message)
    {
        var inner = new Mock<ITelegramBotClient>();
        inner
            .Setup(x => x.SendRequest(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        return inner;
    }

    private static Mock<ITelegramBotClient> Disposable()
    {
        var client = new Mock<ITelegramBotClient>();
        client.As<IDisposable>();
        client.As<IAsyncDisposable>().Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return client;
    }

    private static async Task<IReadOnlyList<TelegramHistoryEntry>> EntriesAsync(HistoryHost host)
    {
        await host.Writer.FlushAsync(CancellationToken.None);
        return host.Store.Entries;
    }

    // Serializing it throws, so no entry can be built for it.
    private sealed class UnserializableRequest : IRequest<bool>
    {
        public HttpMethod HttpMethod => HttpMethod.Post;

        public string MethodName => "unserializable";

        public bool IsWebhookResponse { get; set; }

        public string Value => throw new InvalidOperationException("Can't be read.");

        public HttpContent? ToHttpContent() => null;
    }
}
