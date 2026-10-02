using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

/// <summary>
/// Answers the requests the startup initializers make and records what it was asked.
/// </summary>
internal sealed class FakeBotClient(
    string? webhookUrl = null,
    bool unreachable = false,
    BotCommand[]? menu = null,
    UpdateType[]? webhookAllowedUpdates = null
) : ITelegramBotClient
{
    public List<string> Requests { get; } = [];

    public bool? DroppedPendingUpdates { get; private set; }

    public List<BotCommandScope?> MenuScopes { get; } = [];

    public BotCommand[]? SentMenu { get; private set; }

    public SetWebhookRequest? SetWebhook { get; private set; }

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

        switch (request)
        {
            case DeleteWebhookRequest delete:
                DroppedPendingUpdates = delete.DropPendingUpdates;
                break;
            case GetMyCommandsRequest get:
                MenuScopes.Add(get.Scope);
                break;
            case SetMyCommandsRequest set:
                MenuScopes.Add(set.Scope);
                SentMenu = [.. set.Commands];
                break;
            case SetWebhookRequest set:
                SetWebhook = set;
                break;
        }

        object response = request switch
        {
            GetMeRequest => new User
            {
                Id = BotId,
                IsBot = true,
                FirstName = "Test Bot",
            },
            GetWebhookInfoRequest => new WebhookInfo
            {
                Url = webhookUrl ?? string.Empty,
                AllowedUpdates = webhookAllowedUpdates,
            },
            DeleteWebhookRequest => true,
            SetWebhookRequest => true,
            GetMyCommandsRequest => menu ?? [],
            SetMyCommandsRequest => true,
            SendMessageRequest send => new Message
            {
                Id = Requests.Count,
                Date = DateTime.UtcNow,
                Chat = new Chat { Id = send.ChatId.Identifier ?? 0, Type = ChatType.Private },
                Text = send.Text,
            },
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
