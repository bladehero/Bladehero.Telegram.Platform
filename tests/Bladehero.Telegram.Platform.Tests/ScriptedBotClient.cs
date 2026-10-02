using System.Text.Json;
using System.Text.Json.Nodes;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Tests;

// Answers each method from what the test scripted for it, else as Telegram would on success; records every call.
internal sealed class ScriptedBotClient : ITelegramBotClient
{
    private readonly Dictionary<string, Queue<object>> _answers = [];
    private readonly List<Call> _calls = [];
    private int _nextMessageId = 10;

    public IReadOnlyList<Call> Calls => _calls;

    public IEnumerable<string> Methods => _calls.Select(x => x.Method);

    // The next calls of `method` get these, in order: a result, or an exception to throw.
    public ScriptedBotClient Answer(string method, params object[] answers)
    {
        if (!_answers.TryGetValue(method, out var queue))
        {
            _answers[method] = queue = new Queue<object>();
        }

        foreach (var answer in answers)
        {
            queue.Enqueue(answer);
        }

        return this;
    }

    // Telegram's refusal, e.g. "message is not modified".
    public static ApiRequestException Refusal(string description, int code = 400) =>
        new(code == 400 ? $"Bad Request: {description}" : description, code);

    public Task<TResponse> SendRequest<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    )
    {
        var fields = JsonSerializer.SerializeToNode(request, request.GetType(), JsonBotAPI.Options)!.AsObject();
        var call = new Call(
            request.MethodName,
            Value<long>(fields["chat_id"]),
            Value<int>(fields["message_id"]),
            fields["inline_message_id"]?.GetValue<string>(),
            fields["text"]?.GetValue<string>(),
            fields.ContainsKey("reply_markup")
        );
        _calls.Add(call);

        if (_answers.TryGetValue(request.MethodName, out var queue) && queue.TryDequeue(out var answer))
        {
            return answer is Exception error
                ? Task.FromException<TResponse>(error)
                : Task.FromResult((TResponse)answer);
        }

        object success =
            typeof(TResponse) == typeof(Message)
                ? new Message
                {
                    Id = request is SendMessageRequest ? _nextMessageId++ : call.MessageId ?? 0,
                    Date = DateTime.UtcNow,
                    Chat = new Chat { Id = call.ChatId ?? 0, Type = ChatType.Private },
                    Text = call.Text,
                }
                : true;
        return Task.FromResult((TResponse)success);
    }

    private static T? Value<T>(JsonNode? node)
        where T : struct => node is JsonValue value && value.TryGetValue(out T number) ? number : null;

    public sealed record Call(
        string Method,
        long? ChatId,
        int? MessageId,
        string? InlineMessageId,
        string? Text,
        bool HasMarkup
    );

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

    public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    #endregion
}
