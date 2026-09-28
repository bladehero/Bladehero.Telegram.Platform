using System.Runtime.ExceptionServices;
using Bladehero.Telegram.Platform.Receiving.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A bot running for a component test: your service registrations and the library's real hosting, with
/// <see cref="FakeBotApi"/> standing in for Telegram.
/// </summary>
public sealed class TelegramTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly ErrorLog _errors;

    private TelegramTestHost(IHost host, FakeBotApi api, ErrorLog errors)
    {
        _host = host;
        _errors = errors;
        Api = api;
    }

    /// <summary>The fake Telegram the bot talks to: every call it made, and the chats as they now stand.</summary>
    public FakeBotApi Api { get; }

    /// <summary>The bot's root services, for arranging state or checking it afterwards.</summary>
    public IServiceProvider Services => _host.Services;

    internal TimeSpan UpdateTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts the bot the way long polling runs it in production: <paramref name="configureServices"/> registers the
    /// application as its own composition root does — including <c>AddTelegramLongPollingReceiving</c> — and the
    /// started host's real polling loop pulls updates from the fake.
    /// </summary>
    /// <remarks>
    /// Only the bot client is swapped, for one talking to <see cref="Api"/>, so the token in your configuration is
    /// never used. Every hosted service starts, as in production. The container is validated on build, so a
    /// registration that cannot be resolved fails here rather than in the middle of a test.
    /// </remarks>
    public static async Task<TelegramTestHost> ForLongPollingAsync(
        Action<IServiceCollection> configureServices,
        CancellationToken token = default
    )
    {
        var api = new FakeBotApi();
        var errors = new ErrorLog();

        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        configureServices(builder.Services);

        builder.Services.Replace(ServiceDescriptor.Singleton(new TelegramBotClientAccessor(api.CreateClient())));
        builder.Services.Replace(
            ServiceDescriptor.Scoped<ITelegramErrorHandler>(_ => new RecordingErrorHandler(errors))
        );
        builder.ConfigureContainer(
            new DefaultServiceProviderFactory(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            )
        );

        var host = builder.Build();
        await host.StartAsync(token);

        return new TelegramTestHost(host, api, errors);
    }

    /// <summary>
    /// Delivers <paramref name="update"/> to the bot and returns once the bot has finished handling it, rethrowing
    /// whatever a command threw so a crash fails the test.
    /// </summary>
    /// <exception cref="TimeoutException">The bot never picked the update up.</exception>
    public async Task SendAsync(Update update, CancellationToken token = default)
    {
        var updateId = Api.Enqueue(update);

        try
        {
            await Api.HandledAsync(updateId).WaitAsync(UpdateTimeout, token);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"The bot did not finish update {updateId} within {UpdateTimeout.TotalSeconds:0} seconds. "
                    + "Is long polling registered, for example with AddTelegramLongPollingReceiving?"
            );
        }

        _errors.ThrowIfAny();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();

        if (_host is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
        else
        {
            _host.Dispose();
        }
    }

    private sealed class ErrorLog
    {
        private readonly List<Exception> _errors = [];

        public void Add(Exception exception)
        {
            lock (_errors)
            {
                _errors.Add(exception);
            }
        }

        public void ThrowIfAny()
        {
            Exception? first;
            lock (_errors)
            {
                first = _errors.FirstOrDefault();
                _errors.Clear();
            }

            if (first is not null)
            {
                ExceptionDispatchInfo.Capture(first).Throw();
            }
        }
    }

    private sealed class RecordingErrorHandler(ErrorLog errors) : ITelegramErrorHandler
    {
        public Task HandleAsync(TelegramError telegramError)
        {
            errors.Add(telegramError.Exception);
            return Task.CompletedTask;
        }
    }
}
