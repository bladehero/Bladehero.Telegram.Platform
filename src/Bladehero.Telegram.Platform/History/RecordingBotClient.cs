using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.History;

// Records every Bot API call with its result or failure; polling's getUpdates and file downloads aren't recorded.
// Not disposable, so the container doesn't keep a transient one alive; see OwningRecordingBotClient.
internal class RecordingBotClient(ITelegramBotClient inner, TelegramHistoryWriter writer) : ITelegramBotClient
{
    internal ITelegramBotClient Inner => inner;

    public bool LocalBotServer => inner.LocalBotServer;

    public long BotId => inner.BotId;

    public TimeSpan Timeout
    {
        get => inner.Timeout;
        set => inner.Timeout = value;
    }

    public IExceptionParser ExceptionsParser
    {
        get => inner.ExceptionsParser;
        set => inner.ExceptionsParser = value;
    }

    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest
    {
        add => inner.OnMakingApiRequest += value;
        remove => inner.OnMakingApiRequest -= value;
    }

    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived
    {
        add => inner.OnApiResponseReceived += value;
        remove => inner.OnApiResponseReceived -= value;
    }

    public Task<TResponse> SendRequest<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    ) =>
        request is GetUpdatesRequest
            ? inner.SendRequest(request, cancellationToken)
            : RecordAsync(request, cancellationToken);

    public Task<bool> TestApi(CancellationToken cancellationToken = default) => inner.TestApi(cancellationToken);

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) =>
        inner.DownloadFile(filePath, destination, cancellationToken);

    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) =>
        inner.DownloadFile(file, destination, cancellationToken);

    private async Task<TResponse> RecordAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken
    )
    {
        TResponse result;
        try
        {
            result = await inner.SendRequest(request, cancellationToken);
        }
        catch (Exception error)
        {
            writer.RecordCall(request, null, error);
            throw;
        }

        writer.RecordCall(request, result, null);
        return result;
    }
}
