using System.Diagnostics;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;

namespace Bladehero.Telegram.Platform.Testing;

// Reading the calls: from a mark, as Telegram.Bot requests, or by waiting for one.
public sealed partial class FakeBotApi
{
    private const int CallsListed = 10;

    private readonly List<IRequest> _requests = [];
    private TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A point in <see cref="Calls"/>: how many there are now.</summary>
    public int Mark()
    {
        lock (_gate)
        {
            return _calls.Count;
        }
    }

    /// <summary>The calls since <paramref name="mark"/>, oldest first.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mark"/> is negative or beyond <see cref="Calls"/>.
    /// </exception>
    public IReadOnlyList<BotApiCall> CallsSince(int mark)
    {
        lock (_gate)
        {
            ThrowIfNotAMark(mark, nameof(mark));
            return [.. _calls.Skip(mark).Select(Copy)];
        }
    }

    /// <summary>The requests the bot made, as Telegram.Bot objects, <c>getUpdates</c> aside.</summary>
    /// <remarks>One per attempt: a request Telegram.Bot retries after a 429 is there once per try.</remarks>
    public IReadOnlyList<TRequest> Sent<TRequest>()
        where TRequest : class, IRequest
    {
        lock (_gate)
        {
            return [.. _requests.OfType<TRequest>()];
        }
    }

    /// <summary>
    /// Waits for a call to <paramref name="method"/> that <paramref name="match"/> accepts, e.g. an
    /// <c>answerCallbackQuery</c> from a background job.
    /// </summary>
    /// <param name="method">The method, e.g. <c>sendMessage</c>, in any case.</param>
    /// <param name="match">Accepts the call; any call to the method, when <c>null</c>.</param>
    /// <param name="after">A <see cref="Mark"/>: only calls after it count.</param>
    /// <param name="timeout">How long to wait: 30 seconds, or no limit while a debugger is attached.</param>
    /// <param name="token">Stops waiting.</param>
    /// <returns>The newest matching call already made; failing that, the first to come.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="after"/> isn't a mark, or <paramref name="timeout"/> isn't positive.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// None came in time; the error lists the calls since <paramref name="after"/>, or since the wait began.
    /// </exception>
    public async Task<BotApiCall> WaitForCallAsync(
        string method,
        Func<BotApiCall, bool>? match = null,
        int? after = null,
        TimeSpan? timeout = null,
        CancellationToken token = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var limit = timeout ?? (Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(30));
        if (limit <= TimeSpan.Zero && limit != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), limit, "Waiting needs some time.");
        }

        int seen;
        int listed;
        lock (_gate)
        {
            if (after is { } mark)
            {
                ThrowIfNotAMark(mark, nameof(after));
            }

            seen = after ?? 0;
            listed = after ?? _calls.Count;
        }

        var clock = Stopwatch.StartNew();
        for (var firstLook = true; ; firstLook = false)
        {
            BotApiCall[] fresh;
            Task next;
            lock (_gate)
            {
                fresh = [.. _calls.Skip(seen).Select(Copy)];
                seen = _calls.Count;
                next = _called.Task;
            }

            var matching = fresh.Where(call =>
                string.Equals(call.Method, method, StringComparison.OrdinalIgnoreCase) && (match?.Invoke(call) ?? true)
            );

            // Nothing matched before a later look, so whatever matches then is new.
            if ((firstLook ? matching.LastOrDefault() : matching.FirstOrDefault()) is { } found)
            {
                return found;
            }

            try
            {
                await next.WaitAsync(TelegramTestHost.Left(limit, clock), token);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(NoCall(method, match is not null, limit, listed));
            }
        }
    }

    // Under _gate.
    private void Record(BotApiCall call)
    {
        _calls.Add(call);

        var called = _called;
        _called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        called.TrySetResult();
    }

    private ValueTask RecordRequestAsync(ITelegramBotClient client, ApiRequestEventArgs args, CancellationToken token)
    {
        if (args.Request is not GetUpdatesRequest)
        {
            lock (_gate)
            {
                _requests.Add(args.Request);
            }
        }

        return ValueTask.CompletedTask;
    }

    private string NoCall(string method, bool matching, TimeSpan limit, int listed)
    {
        var since = CallsSince(listed).Select(call => call.Method).ToArray();
        var what = matching ? $"{method} call matching the predicate" : $"{method} call";

        return $"No {what} came within {TelegramTestHost.Describe(limit)}. "
            + (
                since.Length == 0
                    ? "The bot made no calls since then."
                    : $"The calls since then were: {string.Join(", ", since.Take(CallsListed))}"
                        + (since.Length > CallsListed ? ", …." : ".")
            );
    }

    private void ThrowIfNotAMark(int mark, string name)
    {
        if (mark < 0 || mark > _calls.Count)
        {
            throw new ArgumentOutOfRangeException(name, mark, $"A mark is 0 to {_calls.Count}, the calls so far.");
        }
    }

    private static BotApiCall Copy(BotApiCall call) =>
        call with
        {
            Parameters = call.Parameters.DeepClone().AsObject(),
        };
}
