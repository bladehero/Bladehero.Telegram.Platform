using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Hub;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bladehero.Telegram.Platform.Testing.Tests;

// The callee's side: what a pass must be to count, checked against the fake hub's keys.
public sealed class HubPassTests : IAsyncLifetime
{
    private const string Audience = "familybudget";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeBotApi _hub = new();
    private Callee _callee = null!;

    public async Task InitializeAsync()
    {
        _hub.HubTime = _time;
        _callee = await Callee.StartAsync(_hub, _time);
    }

    public async Task DisposeAsync() => await _callee.DisposeAsync();

    [Fact]
    public async Task AValidPass_ShouldLetTheCallerIn_AsKeysDo()
    {
        // Act
        using var response = await _callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "credits:read"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync())
            .Should()
            .Be($"income {HubPassDefaults.ClientIdOf("income")} income credits:read");
    }

    [Fact]
    public async Task APassForAnotherApp_ShouldBeRefused()
    {
        // Act
        using var response = await _callee.GetAsync(
            "/credits",
            _hub.IssueHubPass("income", "nutrition", "credits:read")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task APassFromAnotherHub_ShouldBeRefused()
    {
        // Arrange
        var claims = Claims("income", Audience);
        claims["iss"] = "https://qa-hub.test";

        // Act
        using var response = await _callee.GetAsync("/credits", _hub.SignHubPass(_hub.Header(), claims));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnExpiredPass_ShouldBeRefused()
    {
        // Arrange
        var pass = _hub.IssueHubPass(
            "income",
            Audience,
            ["credits:read"],
            _time.GetUtcNow() - TimeSpan.FromMinutes(11),
            TimeSpan.FromMinutes(10)
        );

        // Act
        using var response = await _callee.GetAsync("/credits", pass);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task APassWithAnotherAlgorithm_ShouldBeRefused()
    {
        // Arrange: "none", with the hub's real signature left on, the oldest trick there is.
        var header = _hub.Header();
        header["alg"] = "none";

        // Act
        using var response = await _callee.GetAsync("/credits", _hub.SignHubPass(header, Claims("income", Audience)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task APassSignedWithAKeyTheHubDoesNotList_ShouldBeRefused()
    {
        // Arrange
        var header = _hub.Header();
        header["kid"] = "made-up";

        // Act
        using var response = await _callee.GetAsync("/credits", _hub.SignHubPass(header, Claims("income", Audience)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task APassWithoutTheRoutesScope_ShouldBeForbidden()
    {
        // Act
        using var response = await _callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "balances:read"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AForwardedPass_ShouldBeRefused_ThoughTheForwardedHeadersMiddlewareHidIt()
    {
        // Arrange: the test server has no remote address, so the middleware takes X-Forwarded-For away and writes no
        // X-Original-For: by the time the scheme looks, the request seems to come from inside.
        var pass = _hub.IssueHubPass("income", Audience, "credits:read");

        // Act
        using var response = await _callee.GetAsync("/credits", pass, ("X-Forwarded-For", "203.0.113.5"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("X-Original-For", "203.0.113.5")]
    [InlineData("Forwarded", "for=203.0.113.5")]
    [InlineData("Host", "expenses.example")]
    public async Task APassThroughTheEdge_ShouldBeRefused(string header, string value)
    {
        // Act
        using var response = await _callee.GetAsync(
            "/credits",
            _hub.IssueHubPass("income", Audience, "credits:read"),
            (header, value)
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task APassActingForAPerson_ShouldBeRefused_UntilSuchCallersAreAllowed()
    {
        // Arrange
        var claims = Claims("home", Audience);
        claims["person"] = 7_000_000_001;

        // Act
        using var response = await _callee.GetAsync("/credits", _hub.SignHubPass(_hub.Header(), claims));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnAppsOwnKey_ShouldStillWork_BesideThePasses()
    {
        // Act
        using var key = await _callee.GetAsync("/either", "ex_good");
        using var pass = await _callee.GetAsync("/either", _hub.IssueHubPass("income", Audience, "credits:read"));

        // Assert
        key.StatusCode.Should().Be(HttpStatusCode.OK);
        pass.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ANewKeyId_ShouldReadTheKeysOnce()
    {
        // Arrange
        using (await _callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "credits:read"))) { }
        var reads = _callee.KeyReads;
        _hub.RotateHubPassKey();
        _time.Advance(TimeSpan.FromSeconds(31));

        // Act
        using var first = await _callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "credits:read"));
        using var second = await _callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "credits:read"));

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        _callee.KeyReads.Should().Be(reads + 1);
    }

    [Fact]
    public async Task WithTheHubDown_TheKeysItGaveShouldStillCheckPasses()
    {
        // Arrange
        using (await _callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "credits:read"))) { }
        _hub.HubKeysUnreachable = true;
        _time.Advance(TimeSpan.FromHours(2));

        // Act
        using var response = await _callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "credits:read"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AHubOutage_ShouldBeWarnedAboutOnce_AndItsEndSaidOnce()
    {
        // Arrange: the keys are read, then the hub goes down; a pass with a key id nobody lists makes the callee ask again
        // every 30 seconds, as the refresh does while it has none.
        using (await _callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "credits:read"))) { }
        _hub.HubKeysUnreachable = true;

        // Act
        await AskForANewKeyAsync(times: 3);
        _hub.HubKeysUnreachable = false;
        await AskForANewKeyAsync(times: 2);
        _hub.HubKeysUnreachable = true;
        await AskForANewKeyAsync(times: 2);

        // Assert
        _callee.KeyReads.Should().BeGreaterThanOrEqualTo(8);
        _callee
            .Logs.Entries.Select(entry => entry[..entry.IndexOf(':')])
            .Should()
            .Equal("Warning", "Information", "Warning");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://api.telegram.org")]
    public async Task WithoutAHub_NoKeysShouldBeAskedFor_AndNoPassTaken(string? apiBaseUrl)
    {
        // Arrange: a developer's PC, the bot talking to Telegram itself.
        await using var callee = await Callee.StartAsync(_hub, _time, apiBaseUrl);

        // Act
        using var response = await callee.GetAsync("/credits", _hub.IssueHubPass("income", Audience, "credits:read"));
        _time.Advance(TimeSpan.FromMinutes(5));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        callee.KeyReads.Should().Be(0);
        callee.Logs.Entries.Should().NotContain(entry => entry.StartsWith("Warning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACaller_ShouldGetAPassFromTheHub_AndTheCalleeTakeIt()
    {
        // Arrange: Income behind the fake hub, granted a scope at Expenses.
        _hub.HubSlug = "income";
        _hub.GrantHubPass(Audience, "credits:read", "balances:read");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_hub.CreateClient());
        services
            .AddHttpClient("expenses", client => client.BaseAddress = new Uri("http://familybudget:8080"))
            .AddHubPass(Audience)
            .ConfigurePrimaryHttpMessageHandler(() => _callee.Handler());
        await using var provider = services.BuildServiceProvider();
        var expenses = provider.GetRequiredService<IHttpClientFactory>().CreateClient("expenses");

        // Act
        using var response = await expenses.GetAsync("/credits");
        using var again = await expenses.GetAsync("/credits");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().EndWith("income credits:read balances:read");
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        _hub.Calls.Count(call => call.Method == "getHubPass").Should().Be(1, "the pass is kept");
    }

    private async Task AskForANewKeyAsync(int times)
    {
        for (var i = 0; i < times; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(31));
            var header = _hub.Header();
            header["kid"] = "not-listed";
            using (await _callee.GetAsync("/credits", _hub.SignHubPass(header, Claims("income", Audience)))) { }
        }
    }

    private JsonObject Claims(string caller, string audience)
    {
        var now = _time.GetUtcNow();
        return new JsonObject
        {
            ["iss"] = FakeBotApi.HubIssuer,
            ["sub"] = caller,
            ["aud"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = (now + TimeSpan.FromMinutes(10)).ToUnixTimeSeconds(),
            ["scope"] = "credits:read",
        };
    }

    // Expenses, as far as passes go: one route that needs a scope, one that takes keys or passes.
    private sealed class Callee : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly Counting _keys;

        private Callee(WebApplication app, Counting keys, KeyLogs logs)
        {
            _app = app;
            _keys = keys;
            Logs = logs;
            _client = app.GetTestClient();
        }

        public int KeyReads => _keys.Reads;

        public KeyLogs Logs { get; }

        // The bot's ApiBaseUrl: the hub's inside address, as on the VPS, or blank or Telegram's, as on a developer's PC.
        public static async Task<Callee> StartAsync(
            FakeBotApi hub,
            TimeProvider time,
            string? apiBaseUrl = "http://telegramhub:8080"
        )
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            var logs = new KeyLogs();
            builder.Logging.AddProvider(logs);
            builder.Services.Configure<TelegramBotConfiguration>(bot => bot.ApiBaseUrl = apiBaseUrl);
            builder.Services.AddSingleton(time);
            var keys = new Counting(hub.CreateHandler());
            builder.Services.AddHttpClient(HubPassFormat.KeysClient).ConfigurePrimaryHttpMessageHandler(() => keys);
            builder
                .Services.AddAuthentication()
                .AddHubPasses(options =>
                {
                    options.Audience = Audience;
                    options.PublicHosts = ["expenses.example"];
                })
                .AddScheme<AuthenticationSchemeOptions, AppKeys>(AppKeys.SchemeName, null);
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor });
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/credits", (ClaimsPrincipal user) => Describe(user)).RequireHubScope("credits:read");
            app.MapGet("/either", () => "ok")
                .RequireAuthorization(policy =>
                    policy
                        .AddAuthenticationSchemes(HubPassDefaults.Scheme, AppKeys.SchemeName)
                        .RequireClaim(HubPassDefaults.ClientIdClaim)
                );
            await app.StartAsync();
            return new Callee(app, keys, logs);
        }

        public HttpMessageHandler Handler() => _app.GetTestServer().CreateHandler();

        public async Task<HttpResponseMessage> GetAsync(
            string path,
            string token,
            (string Name, string Value)? header = null
        )
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (header is ("Host", var host))
            {
                request.Headers.Host = host;
            }
            else if (header is var (name, value))
            {
                request.Headers.Add(name, value);
            }

            return await _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
        }

        private static string Describe(ClaimsPrincipal user) =>
            string.Join(
                ' ',
                [
                    user.HubApp(),
                    user.FindFirstValue(HubPassDefaults.ClientIdClaim),
                    user.FindFirstValue(HubPassDefaults.ClientNameClaim),
                    .. user.FindAll(HubPassDefaults.ScopeClaim).Select(claim => claim.Value),
                ]
            );
    }

    // What the verifier's key reading logs, warnings and above, as "Level: message".
    private sealed class KeyLogs : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyList<string> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("Bladehero.Telegram.Platform.Hub.HubPassKeys", StringComparison.Ordinal)
                ? this
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => _entries.Enqueue($"{logLevel}: {formatter(state, exception)}");

        public void Dispose() { }
    }

    private sealed class Counting(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _reads;

        public int Reads => _reads;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _reads);
            return base.SendAsync(request, cancellationToken);
        }
    }

    // An app's own keys, as Expenses' ex_ keys: they answer nothing for what isn't theirs.
    private sealed class AppKeys(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ApiKey";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ex_", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(
                header == "Bearer ex_good"
                    ? AuthenticateResult.Success(
                        new AuthenticationTicket(
                            new ClaimsPrincipal(
                                new ClaimsIdentity([new Claim(HubPassDefaults.ClientIdClaim, "key")], SchemeName)
                            ),
                            SchemeName
                        )
                    )
                    : AuthenticateResult.Fail("Unknown key.")
            );
        }
    }
}
