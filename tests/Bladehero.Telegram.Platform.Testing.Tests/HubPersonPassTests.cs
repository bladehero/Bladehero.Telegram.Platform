using System.Buffers.Text;
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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bladehero.Telegram.Platform.Testing.Tests;

// Person passes: a delegate (Home) acts for a person at an app that opts in, on /mcp only, within the scopes the app lets
// people grant, as the principal the person's own key would give.
public sealed class HubPersonPassTests : IAsyncLifetime
{
    private const string Audience = "gym";
    private const long Nick = 7_000_000_001;
    private const long Kate = 7_000_000_002;
    private const long Stranger = 7_000_000_099;

    private static readonly TelegramHubPersonConnection NicksClaude = new(
        Guid.Parse("8b1e0c4e-5d1f-4a4b-9c55-0f3a2b1c9d01"),
        "Claude · Ник"
    );

    private static readonly TelegramHubPersonConnection KatesClaude = new(
        Guid.Parse("2f0c9a77-1e5b-4c3d-8a21-6b7d4e5f6a02"),
        "Claude · Катя"
    );

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeBotApi _hub = new();
    private Gym _gym = null!;

    public async Task InitializeAsync()
    {
        _hub.HubTime = _time;
        _gym = await Gym.StartAsync(_hub, _time);
    }

    public async Task DisposeAsync() => await _gym.DisposeAsync();

    [Fact]
    public async Task APersonPass_ShouldGiveThePrincipalThePersonsKeyWould_NamingThePersonAndNoApp()
    {
        // Act: people:read is the app's word for Nutrition, not one a person may grant.
        using var response = await _gym.GetAsync(
            "/mcp",
            _hub.IssuePersonPass("home", Audience, Nick, NicksClaude, "read", "workouts:write", "people:read")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var principal = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        principal["user"]!.GetValue<string>().Should().Be("user-of-nick");
        principal["client_id"]!
            .GetValue<string>()
            .Should()
            .Be(HubPassDefaults.ConnectionIdOf("home", NicksClaude.Id).ToString());
        principal["client_name"]!.GetValue<string>().Should().Be("Claude · Ник");
        principal["scopes"]!
            .AsArray()
            .Select(scope => scope!.GetValue<string>())
            .Should()
            .Equal("read", "workouts:write");
        principal["person"]!.GetValue<long>().Should().Be(Nick);
        principal["via"]!.GetValue<string>().Should().Be("home");
        principal["app"].Should().BeNull("a person pass is a key the person made, not an app acting for itself");
        principal["hasHubScope"]!.GetValue<bool>().Should().BeFalse();
        _gym.Asked.Single().Scopes.Should().Equal("read", "workouts:write");
    }

    [Fact]
    public async Task EachPersonsPass_ShouldActAsThatPerson()
    {
        // Act
        using var kate = await _gym.GetAsync("/mcp", _hub.IssuePersonPass("home", Audience, Kate, KatesClaude, "read"));

        // Assert
        kate.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await kate.Content.ReadAsStringAsync())!["user"]!.GetValue<string>().Should().Be("user-of-kate");
    }

    [Fact]
    public async Task WithoutAPeopleCallback_APersonPassShouldBeRefused_AsBefore()
    {
        // Arrange
        await using var app = await Gym.StartAsync(_hub, _time, people: false);

        // Act
        using var response = await app.GetAsync(
            "/mcp",
            _hub.IssuePersonPass("home", Audience, Nick, NicksClaude, "read")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/internal/feed")]
    [InlineData("/api/workouts")]
    [InlineData("/mcpx")]
    public async Task OnAPathOutsideThePeoplePaths_APersonPassShouldBeRefused(string path)
    {
        // Act
        using var response = await _gym.GetAsync(
            path,
            _hub.IssuePersonPass("home", Audience, Nick, NicksClaude, "read")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _gym.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task OnARouteThatRequiresAHubScope_APersonPassShouldBeForbidden()
    {
        // Act
        using var response = await _gym.GetAsync(
            "/mcp/feed",
            _hub.IssuePersonPass("home", Audience, Nick, NicksClaude, "read")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FromACallerThatIsNotADelegate_APersonPassShouldBeRefused()
    {
        // Act
        using var response = await _gym.GetAsync(
            "/mcp",
            _hub.IssuePersonPass("nutrition", Audience, Nick, NicksClaude, "read")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _gym.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task ADelegatesPassThatNamesNoPerson_ShouldBeRefused_EvenWhereAppPassesCount()
    {
        // Act
        using var mcp = await _gym.GetAsync("/mcp", _hub.IssueHubPass("home", Audience, "read"));
        using var feed = await _gym.GetAsync("/internal/feed", _hub.IssueHubPass("home", Audience, "people:read"));

        // Assert
        mcp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        feed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnAppPassAndAKey_ShouldWorkAsBefore()
    {
        // Act
        using var feed = await _gym.GetAsync("/internal/feed", _hub.IssueHubPass("nutrition", Audience, "people:read"));
        using var key = await _gym.GetAsync("/mcp", "gy_good");

        // Assert
        feed.StatusCode.Should().Be(HttpStatusCode.OK);
        key.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await key.Content.ReadAsStringAsync())!["user"]!.GetValue<string>().Should().Be("user-of-nick");
    }

    [Fact]
    public async Task APassCarryingNoScopeAPersonMayGrant_ShouldBeRefused()
    {
        // Act
        using var response = await _gym.GetAsync(
            "/mcp",
            _hub.IssuePersonPass("home", Audience, Nick, NicksClaude, "people:read")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _gym.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task APersonTheAppDoesNotKnow_ShouldBeRefused()
    {
        // Act
        using var response = await _gym.GetAsync(
            "/mcp",
            _hub.IssuePersonPass("home", Audience, Stranger, NicksClaude, "read")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhatTheCallbackAddsBeyondThePass_ShouldBeTakenOff()
    {
        // Arrange: the callback hands back the key's full scopes and an app's claim, as a careless one might.
        _gym.Careless = true;

        // Act
        using var response = await _gym.GetAsync(
            "/mcp",
            _hub.IssuePersonPass("home", Audience, Nick, NicksClaude, "read")
        );

        // Assert
        var principal = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        principal["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()).Should().Equal("read");
        principal["app"].Should().BeNull();
        principal["person"]!.GetValue<long>().Should().Be(Nick);
    }

    [Fact]
    public async Task ScopesJoinedOrUnderAnotherClaimType_ShouldBeCutToThePassToo()
    {
        // Arrange: the callback puts the key's scopes in one space-joined claim and under "scp" too.
        _gym.Joined = true;

        // Act
        using var response = await _gym.GetAsync(
            "/mcp",
            _hub.IssuePersonPass("home", Audience, Nick, NicksClaude, "read")
        );

        // Assert
        var principal = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        principal["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()).Should().Equal("read");
        principal["scp"]!.AsArray().Select(scope => scope!.GetValue<string>()).Should().Equal("read");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("another app")]
    [InlineData("no connection")]
    [InlineData("a connection without an id")]
    [InlineData("a connection with a control character")]
    [InlineData("a person that isn't a number")]
    [InlineData("no person id")]
    public async Task AnOddPersonPass_ShouldBeRefused(string what)
    {
        // Arrange
        var now = _time.GetUtcNow();
        var claims = new JsonObject
        {
            ["iss"] = FakeBotApi.HubIssuer,
            ["sub"] = "home",
            ["aud"] = what == "another app" ? "nutrition" : Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = (
                now + (what == "expired" ? -TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(10))
            ).ToUnixTimeSeconds(),
            ["scope"] = "read",
            ["person"] = what switch
            {
                "a person that isn't a number" => JsonValue.Create("7000000001"),
                "no person id" => JsonValue.Create(0),
                _ => JsonValue.Create(Nick),
            },
            ["cnx"] = what switch
            {
                "no connection" => null,
                "a connection without an id" => new JsonObject { ["name"] = "Claude" },
                "a connection with a control character" => new JsonObject
                {
                    ["id"] = NicksClaude.Id.ToString(),
                    ["name"] = "Claude\nНик",
                },
                _ => new JsonObject { ["id"] = NicksClaude.Id.ToString(), ["name"] = NicksClaude.Name },
            },
        };

        // Act
        using var response = await _gym.GetAsync("/mcp", _hub.SignHubPass(_hub.Header(), claims));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _gym.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task APersonPassThroughTheEdge_ShouldBeRefused()
    {
        // Act
        using var response = await _gym.GetAsync(
            "/mcp",
            _hub.IssuePersonPass("home", Audience, Nick, NicksClaude, "read"),
            ("X-Forwarded-For", "203.0.113.5")
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Home_ShouldGetAPersonPassFromTheHub_AndTheAppTakeIt()
    {
        // Arrange: Home behind the fake hub.
        _hub.HubSlug = "home";
        _hub.GrantPersonPass(Nick, Audience, "read", "workouts:write");
        await using var provider = Delegate();
        var passes = provider.GetRequiredService<ITelegramHubPersonPasses>();

        // Act
        var pass = await passes.GetAsync(Audience, Nick, NicksClaude, ["read"]);
        using var response = await _gym.GetAsync("/mcp", pass.Token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var principal = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        principal["user"]!.GetValue<string>().Should().Be("user-of-nick");
        principal["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()).Should().Equal("read");
    }

    [Fact]
    public async Task PersonPasses_ShouldBeKeptPerPersonAndConnection_AndForgottenWithTheConnection()
    {
        // Arrange
        _hub.HubSlug = "home";
        _hub.GrantPersonPass(Nick, Audience, "read");
        _hub.GrantPersonPass(Kate, Audience, "read");
        await using var provider = Delegate();
        var passes = provider.GetRequiredService<ITelegramHubPersonPasses>();

        // Act
        var nicks = await passes.GetAsync(Audience, Nick, NicksClaude);
        var again = await passes.GetAsync(Audience, Nick, NicksClaude);
        var kates = await passes.GetAsync(Audience, Kate, KatesClaude);
        var nicksOther = await passes.GetAsync(Audience, Nick, NicksClaude with { Id = Guid.NewGuid() });
        var asked = Asked();
        passes.Forget(NicksClaude.Id);
        var afterForget = await passes.GetAsync(Audience, Nick, NicksClaude);

        // Assert
        again.Token.Should().Be(nicks.Token);
        PersonIn(nicks.Token).Should().Be(Nick);
        PersonIn(kates.Token).Should().Be(Kate, "one person's pass is never handed out for another");
        nicksOther.Token.Should().NotBe(nicks.Token);
        asked.Should().Be(3);
        afterForget.Token.Should().NotBe(nicks.Token);
        Asked().Should().Be(4);
    }

    [Fact]
    public async Task ARefusedRenewal_ShouldEndTheKeptPersonPass_ButAnOutageNot()
    {
        // Arrange
        _hub.HubSlug = "home";
        _hub.GrantPersonPass(Nick, Audience, "read");
        _hub.GrantPersonPass(Kate, Audience, "read");
        await using var provider = Delegate();
        var passes = provider.GetRequiredService<ITelegramHubPersonPasses>();
        var kates = await passes.GetAsync(Audience, Kate, KatesClaude);
        await passes.GetAsync(Audience, Nick, NicksClaude);
        _time.Advance(TimeSpan.FromMinutes(6));

        // Act: Nick is taken off Gym at the hub; then the hub goes down for Kate's renewal.
        _hub.GrantPersonPass(Nick, Audience);
        var nick = () => passes.GetAsync(Audience, Nick, NicksClaude).AsTask();
        await nick.Should().ThrowAsync<TelegramHubPassUnavailableException>();
        _hub.FailNetwork("getHubPass");
        var kateDuring = await passes.GetAsync(Audience, Kate, KatesClaude);

        // Assert
        kateDuring.Token.Should().Be(kates.Token, "the hub being down isn't a refusal");
    }

    [Fact]
    public async Task AnAppThatIsNotADelegate_ShouldGetNoPersonPass_AndADelegateNoOtherPass()
    {
        // Arrange
        _hub.HubSlug = "income";
        _hub.GrantPersonPass(Nick, Audience, "read");
        _hub.GrantHubPass("familybudget", "credits:read");
        await using var provider = Delegate();
        var person = () =>
            provider.GetRequiredService<ITelegramHubPersonPasses>().GetAsync(Audience, Nick, NicksClaude).AsTask();

        // Act and assert
        await person.Should().ThrowAsync<TelegramHubPassUnavailableException>();
        _hub.HubSlug = "home";
        var own = () => provider.GetRequiredService<ITelegramHubPasses>().GetAsync("familybudget").AsTask();
        await own.Should().ThrowAsync<TelegramHubPassUnavailableException>();
    }

    [Fact]
    public async Task TheAppsForAPerson_ShouldBeThoseGrantedThere_WithTheirLabels()
    {
        // Arrange
        _hub.HubSlug = "home";
        _hub.GrantPersonPass(Nick, Audience, "read", "workouts:write");
        _hub.GrantPersonPass(Kate, "familybudget", "read");
        _hub.DescribeHubApp(
            Audience,
            "Gym",
            "https://gym.example",
            new TelegramHubScope("read", "Видеть тренировки", ForPeople: true)
        );
        await using var provider = Delegate();

        // Act
        var apps = await provider.GetRequiredService<ITelegramHubPersonPasses>().GetAppsAsync(Nick);

        // Assert
        var gym = apps.Should().ContainSingle().Which;
        gym.Slug.Should().Be(Audience);
        gym.Title.Should().Be("Gym");
        gym.Site.Should().Be("https://gym.example");
        gym.Scopes.Select(scope => (scope.Scope, scope.Label, scope.ForPeople))
            .Should()
            .Equal(("read", "Видеть тренировки", true), ("workouts:write", "workouts:write", true));
    }

    private ServiceProvider Delegate()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_hub.CreateClient());
        services.AddTelegramHubPasses();
        return services.BuildServiceProvider();
    }

    private int Asked() => _hub.Calls.Count(call => call.Method == "getHubPass");

    private static long PersonIn(string pass) =>
        JsonNode.Parse(Base64Url.DecodeFromChars(pass.Split('.')[1]))!["person"]!.GetValue<long>();

    // Gym, as far as person passes go: /mcp takes its gy_ keys and person passes, /internal/feed app passes, and the
    // callback finds the person's user as a key finds its maker.
    private sealed class Gym : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Gym(WebApplication app)
        {
            _app = app;
            _client = app.GetTestClient();
        }

        public List<HubPassPerson> Asked { get; } = [];

        public bool Careless { get; set; }

        public bool Joined { get; set; }

        public static async Task<Gym> StartAsync(FakeBotApi hub, TimeProvider time, bool people = true)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<TelegramBotConfiguration>(bot => bot.ApiBaseUrl = "http://telegramhub:8080");
            builder.Services.Configure<TelegramHubConfiguration>(manifest =>
                manifest.Scopes = [
                    new("people:read", "Люди"),
                    new("read", "Видеть тренировки", ForPeople: true),
                    new("workouts:write", "Записывать тренировки", ForPeople: true),
                ]
            );
            builder.Services.AddSingleton(time);
            builder
                .Services.AddHttpClient(HubPassFormat.KeysClient)
                .ConfigurePrimaryHttpMessageHandler(hub.CreateHandler);
            Gym? gym = null;
            builder
                .Services.AddAuthentication()
                .AddHubPasses(options =>
                {
                    options.Audience = Audience;
                    if (people)
                    {
                        options.People = (person, _) => Task.FromResult(gym!.Admit(person));
                    }
                })
                .AddScheme<AuthenticationSchemeOptions, GymKeys>(GymKeys.SchemeName, null);
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            void Either(IEndpointConventionBuilder route) =>
                route.RequireAuthorization(policy =>
                    policy
                        .AddAuthenticationSchemes(HubPassDefaults.Scheme, GymKeys.SchemeName)
                        .RequireClaim(HubPassDefaults.ClientIdClaim)
                );
            Either(app.MapGet("/mcp", (ClaimsPrincipal user) => Describe(user)));
            Either(app.MapGet("/mcpx", (ClaimsPrincipal user) => Describe(user)));
            Either(app.MapGet("/api/workouts", (ClaimsPrincipal user) => Describe(user)));
            app.MapGet("/mcp/feed", () => "fed").RequireHubScope("read");
            app.MapGet("/internal/feed", () => "fed")
                .RequireAuthorization(policy =>
                    policy
                        .AddAuthenticationSchemes(HubPassDefaults.Scheme, GymKeys.SchemeName)
                        .RequireClaim(HubPassDefaults.ClientIdClaim)
                );
            await app.StartAsync();
            gym = new Gym(app);
            return gym;
        }

        public async Task<HttpResponseMessage> GetAsync(
            string path,
            string token,
            (string Name, string Value)? header = null
        )
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (header is var (name, value))
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

        private ClaimsPrincipal? Admit(HubPassPerson person)
        {
            lock (Asked)
            {
                Asked.Add(person);
            }

            var user = person.TelegramId switch
            {
                Nick => "user-of-nick",
                Kate => "user-of-kate",
                _ => null,
            };
            if (user is null)
            {
                return null;
            }

            if (Joined)
            {
                return new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new Claim(HubPassDefaults.ClientIdClaim, person.ConnectionId.ToString()),
                            new Claim(HubPassDefaults.ClientNameClaim, person.ConnectionName),
                            new Claim("user", user),
                            new Claim(HubPassDefaults.ScopeClaim, "read workouts:write templates:write"),
                            new Claim("scp", "templates:write read"),
                        ],
                        GymKeys.SchemeName
                    )
                );
            }

            IEnumerable<string> scopes = Careless ? ["read", "workouts:write", "templates:write"] : person.Scopes;
            return GymKeys.Principal(person.ConnectionId.ToString(), person.ConnectionName, user, scopes, Careless);
        }

        private static string Describe(ClaimsPrincipal user) =>
            new JsonObject
            {
                ["user"] = user.FindFirstValue("user"),
                ["client_id"] = user.FindFirstValue(HubPassDefaults.ClientIdClaim),
                ["client_name"] = user.FindFirstValue(HubPassDefaults.ClientNameClaim),
                ["scopes"] = new JsonArray([
                    .. user.FindAll(HubPassDefaults.ScopeClaim).Select(claim => (JsonNode)claim.Value),
                ]),
                ["scp"] = new JsonArray([.. user.FindAll("scp").Select(claim => (JsonNode)claim.Value)]),
                ["person"] = user.HubPerson(),
                ["via"] = user.HubVia(),
                ["app"] = user.HubApp(),
                ["hasHubScope"] = user.HasHubScope("read"),
            }.ToJsonString();
    }

    // Gym's own gy_ keys: a key acts as its maker. They answer nothing for what isn't theirs.
    private sealed class GymKeys(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ApiKey";

        public static ClaimsPrincipal Principal(
            string clientId,
            string clientName,
            string user,
            IEnumerable<string> scopes,
            bool withAppClaim = false
        ) =>
            new(
                new ClaimsIdentity(
                    [
                        new Claim(HubPassDefaults.ClientIdClaim, clientId),
                        new Claim(HubPassDefaults.ClientNameClaim, clientName),
                        new Claim("user", user),
                        .. scopes.Select(scope => new Claim(HubPassDefaults.ScopeClaim, scope)),
                        .. withAppClaim ? [new Claim(HubPassDefaults.HubAppClaim, "gym")] : Array.Empty<Claim>(),
                    ],
                    SchemeName
                )
            );

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer gy_", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(
                header == "Bearer gy_good"
                    ? AuthenticateResult.Success(
                        new AuthenticationTicket(
                            Principal("key-1", "Claude", "user-of-nick", ["read", "workouts:write"]),
                            SchemeName
                        )
                    )
                    : AuthenticateResult.Fail("Unknown key.")
            );
        }
    }
}
