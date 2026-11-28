using Bladehero.Telegram.Platform.Hub;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Telegram.Bot;

namespace Bladehero.Telegram.Platform.Testing.Tests;

// The caller's side: one pass per audience, kept, renewed in time, and the last one used while the hub is down.
public sealed class TelegramHubPassesTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeBotApi _hub = new();
    private readonly ServiceProvider _provider;

    public TelegramHubPassesTests()
    {
        _hub.HubTime = _time;
        _hub.HubSlug = "income";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_hub.CreateClient());
        services.AddTelegramHubPasses();
        _provider = services.BuildServiceProvider();
    }

    private ITelegramHubPasses Passes => _provider.GetRequiredService<ITelegramHubPasses>();

    private int Asked => _hub.Calls.Count(call => call.Method == "getHubPass");

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task APass_ShouldBeKept_AndRenewedFiveMinutesBeforeItsEnd()
    {
        // Arrange
        _hub.GrantHubPass("familybudget", "credits:read");

        // Act
        var first = await Passes.GetAsync("familybudget");
        var kept = await Passes.GetAsync("familybudget");
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var renewed = await Passes.GetAsync("familybudget");

        // Assert
        kept.Token.Should().Be(first.Token);
        renewed.Token.Should().NotBe(first.Token);
        first.Scopes.Should().Equal("credits:read");
        Asked.Should().Be(2);
    }

    [Fact]
    public async Task WithTheHubDown_TheLastPassShouldServeWhileItIsGood()
    {
        // Arrange
        _hub.GrantHubPass("familybudget", "credits:read");
        var first = await Passes.GetAsync("familybudget");
        _hub.FailNetwork("getHubPass");
        _time.Advance(TimeSpan.FromMinutes(6));

        // Act
        var during = await Passes.GetAsync("familybudget");
        _time.Advance(TimeSpan.FromMinutes(4));
        var after = () => Passes.GetAsync("familybudget").AsTask();

        // Assert
        during.Token.Should().Be(first.Token);
        await after.Should().ThrowAsync<TelegramHubPassUnavailableException>();
    }

    [Fact]
    public async Task AfterAFailedRenewal_TheHubShouldNotBeAskedAgainForThirtySeconds()
    {
        // Arrange
        _hub.GrantHubPass("familybudget", "credits:read");
        var first = await Passes.GetAsync("familybudget");
        _hub.FailNetwork("getHubPass", times: 2);
        _time.Advance(TimeSpan.FromMinutes(6));
        await Passes.GetAsync("familybudget");

        // Act
        _time.Advance(TimeSpan.FromSeconds(20));
        var within = await Passes.GetAsync("familybudget");
        var askedWithin = Asked;
        _time.Advance(TimeSpan.FromSeconds(11));
        var failedAgain = await Passes.GetAsync("familybudget");
        _time.Advance(TimeSpan.FromSeconds(31));
        var renewed = await Passes.GetAsync("familybudget");

        // Assert
        within.Token.Should().Be(first.Token);
        askedWithin.Should().Be(2);
        failedAgain.Token.Should().Be(first.Token);
        renewed.Token.Should().NotBe(first.Token);
        Asked.Should().Be(4);
    }

    [Fact]
    public async Task WithTheHubHungOnARenewal_TheGoodPassShouldServeAtOnce_AndTheRenewalGiveUpInFiveSeconds()
    {
        // Arrange
        var hub = new HangingHub(_hub.CreateHandler());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ITelegramBotClient>(
            new TelegramBotClient(new TelegramBotClientOptions(FakeBotApi.Token), new HttpClient(hub))
        );
        services.AddTelegramHubPasses();
        await using var provider = services.BuildServiceProvider();
        var passes = provider.GetRequiredService<ITelegramHubPasses>();
        _hub.GrantHubPass("familybudget", "credits:read");
        var first = await passes.GetAsync("familybudget");
        hub.Hangs = true;
        _time.Advance(TimeSpan.FromMinutes(6));
        var renewal = passes.GetAsync("familybudget").AsTask();
        await hub.Reached.WaitAsync(TimeSpan.FromSeconds(10));

        // Act
        var during = passes.GetAsync("familybudget");

        // Assert
        during.IsCompletedSuccessfully.Should().BeTrue();
        (await during).Token.Should().Be(first.Token);
        renewal.IsCompleted.Should().BeFalse();
        _time.Advance(TimeSpan.FromSeconds(5));
        (await renewal.WaitAsync(TimeSpan.FromSeconds(10))).Token.Should().Be(first.Token);
    }

    [Fact]
    public async Task NoGrant_ShouldBeUnavailable_AsAnHttpFailure()
    {
        // Act
        var get = () => Passes.GetAsync("nutrition").AsTask();

        // Assert
        (await get.Should().ThrowAsync<HttpRequestException>())
            .Which.Should()
            .BeOfType<TelegramHubPassUnavailableException>()
            .Which.Message.Should()
            .Contain("not granted");
    }

    // A hub that, once told to, takes getHubPass and never answers, as a hung host would, until the call gives up.
    private sealed class HangingHub(HttpMessageHandler hub) : DelegatingHandler(hub)
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Hangs { get; set; }

        public Task Reached => _reached.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (Hangs && request.RequestUri!.AbsolutePath.EndsWith("/getHubPass", StringComparison.Ordinal))
            {
                _reached.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
