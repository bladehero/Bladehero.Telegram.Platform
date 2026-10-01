using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving;

// The identity when no ITelegramBotClient is registered to ask.
internal sealed class UnknownBotIdentity : ITelegramBotIdentity
{
    public User? Current => null;

    public ValueTask<User> GetAsync(CancellationToken token) =>
        throw new InvalidOperationException("The bot can't learn who it is: no ITelegramBotClient is registered.");
}

internal sealed class TelegramBotIdentity(ITelegramBotClient client, TimeProvider? time = null) : ITelegramBotIdentity
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    // The app's clock when it registered one; the library registers none.
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private User? _current;
    private Task<User>? _pending;
    private DateTimeOffset? _failedAt;

    public User? Current => Volatile.Read(ref _current);

    public ValueTask<User> GetAsync(CancellationToken token) =>
        Current is { } me ? ValueTask.FromResult(me) : new ValueTask<User>(Shared().WaitAsync(token));

    // Never throws but on cancellation; after a failure, asks again at most once a minute.
    internal async ValueTask<User?> TryGetAsync(CancellationToken token)
    {
        if (Current is { } me)
        {
            return me;
        }

        lock (_gate)
        {
            if (_pending is null && _failedAt is { } failedAt && _time.GetUtcNow() - failedAt < RetryAfterFailure)
            {
                return null;
            }
        }

        try
        {
            return await GetAsync(token);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }

    // The getMe in flight, started if there is none; no caller's token cancels it for the others.
    private Task<User> Shared()
    {
        lock (_gate)
        {
            return _pending ??= Task.Run(FetchAsync, CancellationToken.None);
        }
    }

    private async Task<User> FetchAsync()
    {
        try
        {
            var me = await client.GetMe(CancellationToken.None);
            Volatile.Write(ref _current, me);
            return me;
        }
        catch
        {
            lock (_gate)
            {
                _failedAt = _time.GetUtcNow();
            }

            throw;
        }
        finally
        {
            lock (_gate)
            {
                _pending = null;
            }
        }
    }
}
