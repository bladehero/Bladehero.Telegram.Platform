using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bladehero.Telegram.Platform.Testing;

// A bot that pulls its updates from the fake with a real long-polling loop, on a generic host or in an ASP.NET Core app.
public sealed partial class TelegramTestHost
{
    // An update is handled once the polling loop asks for the next offset.
    private sealed class PollingBot : IRunningBot
    {
        private readonly IAsyncDisposable _app;
        private readonly FakeBotApi _api;

        // Polls from before this host, such as an earlier one's on the same fake.
        private readonly int _pollsBefore;

        // The ASP.NET Core app's entry point; null for a generic host.
        private readonly string? _entryPoint;
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly BackgroundService[] _backgroundServices;

        // Takes a started app, which disposing stops.
        public PollingBot(
            IServiceProvider services,
            IAsyncDisposable app,
            FakeBotApi api,
            int pollsBefore,
            string? entryPoint = null
        )
        {
            Services = services;
            _app = app;
            _api = api;
            _pollsBefore = pollsBefore;
            _entryPoint = entryPoint;

            // Called at once if the app is already stopping.
            services
                .GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping.Register(() => _stopped.TrySetResult());

            // A failed background service stops the bot, even where the host is set to carry on without it.
            _backgroundServices = [.. services.GetServices<IHostedService>().OfType<BackgroundService>()];
            foreach (var service in _backgroundServices)
            {
                service.ExecuteTask?.ContinueWith(
                    _ => _stopped.TrySetResult(),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default
                );
            }
        }

        public IServiceProvider Services { get; }

        public async Task DeliverAsync(
            JsonObject update,
            TimeSpan timeout,
            Action<long> numbered,
            CancellationToken token
        )
        {
            var clock = Stopwatch.StartNew();

            // An update queued before the loop listens can be dropped as pending, as DropPendingUpdates does.
            if (!await UntilAsync(_api.PolledAsync(_pollsBefore), Left(timeout, clock), token))
            {
                throw new TimeoutException($"No one fetched the update within {Describe(timeout)}. {WhyNotPolled()}");
            }

            var updateId = _api.Enqueue(update);
            numbered(updateId);

            if (!await UntilAsync(_api.HandledAsync(updateId), Left(timeout, clock), token))
            {
                throw new TimeoutException(Unfinished(updateId, timeout));
            }
        }

        public ValueTask DisposeAsync() => _app.DisposeAsync();

        private static TimeSpan Left(TimeSpan timeout, Stopwatch clock)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                return timeout;
            }

            var left = timeout - clock.Elapsed;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        // False once the time is up; throws if the host stops first.
        private async Task<bool> UntilAsync(Task done, TimeSpan timeout, CancellationToken token)
        {
            try
            {
                await Task.WhenAny(done, _stopped.Task).WaitAsync(timeout, token);
            }
            catch (TimeoutException)
            {
                return false;
            }

            if (!done.IsCompleted)
            {
                throw Stopped();
            }

            return true;
        }

        private InvalidOperationException Stopped()
        {
            var failed = _backgroundServices.FirstOrDefault(x => x.ExecuteTask is { IsFaulted: true });

            return failed is null
                ? new InvalidOperationException(
                    "The bot's host stopped, so the bot will not finish the update. Did the app call "
                        + "IHostApplicationLifetime.StopApplication?"
                )
                : new InvalidOperationException(
                    $"The bot's host stopped because {failed.GetType().Name} failed, so the bot will not finish the "
                        + "update. The inner exception is its failure.",
                    failed.ExecuteTask!.Exception!.InnerException
                );
        }

        private string Unfinished(int updateId, TimeSpan timeout)
        {
            var (fetched, busyBefore) = _api.Progress(updateId);
            var within = Describe(timeout);

            return fetched ? $"The bot fetched update {updateId} but did not finish it within {within}. {MayBeHanging}"
                : busyBefore
                    ? $"The bot did not fetch update {updateId} within {within}: it is still busy with updates it "
                        + $"fetched earlier. {MayBeHanging}"
                : $"No one fetched update {updateId} within {within}. {WhyNotPolled()}";
        }

        private string WhyNotPolled() =>
            _api.WebhookUrl switch
            {
                { } webhook when _api.RefusedPolling =>
                    $"Telegram still has a webhook for the bot, {webhook}, so it refuses every getUpdates with 409. "
                        + "Did deleting it fail? Api.Calls shows what the bot asked Telegram.",
                { } webhook when _entryPoint is not null && _api.Polls == _pollsBefore =>
                    $"The app set a webhook, {webhook}, and does not poll; start it with "
                        + $"ForWebhookAsync<{_entryPoint}>.",
                _ => "Is long polling registered, for example with AddTelegramLongPollingReceiving?",
            };
    }

    // Stops a generic host before disposing it, as the host does not stop itself.
    private sealed class StoppingHost(IHost host) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await host.StopAsync();
            }
            finally
            {
                if (host is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            }
        }
    }
}
