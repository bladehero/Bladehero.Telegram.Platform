namespace Bladehero.Telegram.Platform.Testing;

// Ways the network fails besides a call that never arrives: a time-out, a response lost after Telegram acted, and a
// broken download.
public sealed partial class FakeBotApi
{
    private readonly Dictionary<string, List<DownloadFailure>> _downloadFailures = [];

    /// <summary>
    /// Makes calls to <paramref name="method"/> time out, as an <see cref="HttpClient"/> time-out would; nothing
    /// changes.
    /// </summary>
    /// <remarks>
    /// Every call, or only the next <paramref name="times"/>; with <paramref name="chatId"/>, only the calls to that
    /// chat. It takes turns with the other failures by the same rules.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The method is <c>getUpdates</c>, which the test host owns, or has no chat while <paramref name="chatId"/> is
    /// given.
    /// </exception>
    public void TimeOut(string method, int? times = null, long? chatId = null) =>
        AddFailure(method, FailureKind.TimedOut, error: null, times, chatId);

    /// <summary>Makes Telegram do the call but lose its response, so a retry sends twice.</summary>
    /// <remarks>
    /// Every call, or only the next <paramref name="times"/>; with <paramref name="chatId"/>, only the calls to that
    /// chat. It takes turns with the other failures by the same rules.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The method is <c>getUpdates</c>, which the test host owns, or has no chat while <paramref name="chatId"/> is
    /// given.
    /// </exception>
    public void LoseResponse(string method, int? times = null, long? chatId = null) =>
        AddFailure(method, FailureKind.ResponseLost, error: null, times, chatId);

    /// <summary>
    /// Makes downloads of <paramref name="fileId"/> fail: refused with <paramref name="error"/>, or broken off
    /// without one.
    /// </summary>
    /// <remarks>Every download, or only the next <paramref name="times"/>.</remarks>
    /// <exception cref="ArgumentException">Telegram has no file with this id.</exception>
    public void FailDownload(string fileId, BotApiError? error = null, int? times = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        if (times <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(times), times, "A failure has to happen at least once.");
        }

        lock (_gate)
        {
            if (_files.Find(fileId) is null)
            {
                throw new ArgumentException($"Telegram has no file {fileId}; it was never sent.", nameof(fileId));
            }

            if (!_downloadFailures.TryGetValue(fileId, out var failures))
            {
                _downloadFailures[fileId] = failures = [];
            }

            failures.Add(new DownloadFailure(error, times));
        }
    }

    // The failed response to a download of the file, or null to serve it.
    private HttpResponseMessage? FailedDownload(string fileId)
    {
        DownloadFailure? failure;
        lock (_gate)
        {
            if (!_downloadFailures.TryGetValue(fileId, out var failures) || failures is not [var first, ..])
            {
                return null;
            }

            failure = first;
            if (failure.Happen())
            {
                failures.Remove(failure);
            }
        }

        return failure.Error is { } error
            ? Respond(error)
            : throw new HttpRequestException($"The download of {fileId} broke off, as FakeBotApi.FailDownload asked.");
    }

    // Never the caller's token: this is a time-out, not a cancellation.
    private static TaskCanceledException TimedOut(string method) =>
        new($"The request to {method} timed out, as FakeBotApi.TimeOut asked.", new TimeoutException());

    private static HttpRequestException ResponseLost(string method) =>
        new($"The response to {method} was lost on the way back, as FakeBotApi.LoseResponse asked; Telegram did it.");

    private enum FailureKind
    {
        Refused,
        Unreachable,
        TimedOut,
        ResponseLost,
    }

    private sealed class DownloadFailure(BotApiError? error, int? times)
    {
        public BotApiError? Error { get; } = error;

        // Whether this was its last time.
        public bool Happen()
        {
            if (times is not null)
            {
                times--;
            }

            return times is 0;
        }
    }
}
