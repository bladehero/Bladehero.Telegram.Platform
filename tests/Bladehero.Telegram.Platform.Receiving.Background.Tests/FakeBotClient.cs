using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

/// <summary>
/// Answers the two requests the long-polling initializer makes and records what it was asked.
/// </summary>
internal sealed class FakeBotClient(string? webhookUrl = null, bool unreachable = false) : ITelegramBotClient
{
    public List<string> Requests { get; } = [];

    public bool? DroppedPendingUpdates { get; private set; }

    public Task<TResponse> SendRequest<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        if (unreachable)
        {
            throw new HttpRequestException("Telegram is unreachable");
        }

        Requests.Add(request.MethodName);

        if (request is DeleteWebhookRequest delete)
        {
            DroppedPendingUpdates = delete.DropPendingUpdates;
        }

        object response = request switch
        {
            GetWebhookInfoRequest => new WebhookInfo { Url = webhookUrl ?? string.Empty },
            DeleteWebhookRequest => true,
            _ => throw new InvalidOperationException($"Unexpected request: {request.MethodName}"),
        };

        return Task.FromResult((TResponse)response);
    }

    #region Unused

    public long BotId => 1234567;

    public bool LocalBotServer => false;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest
    {
        add { }
        remove { }
    }

    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived
    {
        add { }
        remove { }
    }

    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);

    #endregion
}
