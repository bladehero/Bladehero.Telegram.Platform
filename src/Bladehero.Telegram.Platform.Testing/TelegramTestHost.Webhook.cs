using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// A bot whose ASP.NET Core app gets each update posted to the webhook it set.
public sealed partial class TelegramTestHost
{
    // pollsBefore: the fake's polls from before the app started, which it did not make.
    private sealed class WebhookBot(
        IAsyncDisposable factory,
        IServiceProvider services,
        HttpClient client,
        FakeBotApi api,
        int pollsBefore,
        string entryPoint
    ) : IRunningBot
    {
        private const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

        private static readonly TimeSpan AbandonedGrace = TimeSpan.FromSeconds(5);

        // Updates the test stopped waiting for; the bot may still be handling them.
        private readonly List<Task> _abandoned = [];

        public IServiceProvider Services => services;

        public async Task DeliverAsync(
            Func<JsonObject> compose,
            TimeSpan timeout,
            Action<long> numbered,
            CancellationToken token
        )
        {
            if (api.Webhook() is not { } webhook)
            {
                throw new InvalidOperationException(await WhyNoWebhookAsync(timeout, token));
            }

            var (stamped, updateId) = api.StampForWebhook(compose());
            numbered(updateId);

            var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(stamped.ToJsonString(), Encoding.UTF8, "application/json"),
            };

            if (webhook.SecretToken is { } secretToken)
            {
                request.Headers.Add(SecretTokenHeader, secretToken);
            }

            // Giving up only stops waiting: aborting would cancel the bot mid-update and fail the next one.
            var sending = client.SendAsync(request, CancellationToken.None);
            HttpResponseMessage response;
            try
            {
                response = await sending.WaitAsync(timeout, token);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                var cleanup = sending.ContinueWith(
                    sent =>
                    {
                        if (sent.IsCompletedSuccessfully)
                        {
                            sent.Result.Dispose();
                        }
                        else
                        {
                            // Observed, so a later failure is not reported as an unobserved task exception.
                            _ = sent.Exception;
                        }

                        request.Dispose();
                    },
                    TaskScheduler.Default
                );

                lock (_abandoned)
                {
                    _abandoned.Add(cleanup);
                }

                if (exception is TimeoutException)
                {
                    throw new TimeoutException(
                        $"The bot did not answer update {updateId}, posted to {webhook.Url}, within "
                            + $"{Describe(timeout)}. {MayBeHanging}"
                    );
                }

                throw;
            }

            using (request)
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(await FailedDeliveryAsync(webhook.Url, response));
                }
            }
        }

        // An app that polls instead has usually polled by the time it started, but may be a moment late.
        private async Task<string> WhyNoWebhookAsync(TimeSpan timeout, CancellationToken token)
        {
            try
            {
                var grace = AtMost(timeout, WrongHostGrace);
                await api.PolledAsync(pollsBefore).WaitAsync(grace, token);

                return "The app polls for updates instead of setting a webhook; start it with "
                    + $"ForLongPollingAsync<{entryPoint}>.";
            }
            catch (TimeoutException)
            {
                return "The bot has set no webhook, so there is nowhere to post the update. Is webhook receiving "
                    + "registered, for example with AddTelegramWebhookReceiving and UseTelegramWebhook, and did its "
                    + "setWebhook succeed? Api.Calls shows what the bot asked Telegram.";
            }
        }

        private static async Task<string> FailedDeliveryAsync(string url, HttpResponseMessage response)
        {
            const int longestBody = 1000;

            var failure =
                $"The bot answered the update posted to {url} with {(int)response.StatusCode} "
                + $"{response.ReasonPhrase}, which Telegram takes as a failed delivery.";

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                return failure + " Is the update endpoint mapped there, for example with UseTelegramWebhook?";
            }

            if (response.Headers.Location is { } location)
            {
                return $"{failure} It redirects to {location}, and Telegram follows no redirect.";
            }

            var body = await response.Content.ReadAsStringAsync();
            return body.Length == 0 ? failure
                : body.Length <= longestBody ? $"{failure} It said:\n{body}"
                : $"{failure} It said:\n{body[..longestBody]}…";
        }

        // Lets cancelled in-flight updates finish before the app's container is disposed.
        public async ValueTask DisposeAsync()
        {
            client.Dispose();

            Task[] abandoned;
            lock (_abandoned)
            {
                abandoned = [.. _abandoned];
            }

            await Task.WhenAll(abandoned)
                .WaitAsync(AbandonedGrace)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await factory.DisposeAsync();
        }
    }
}
