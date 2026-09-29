using System.Text.Json;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

public static class ConversationExtensions
{
    /// <summary>Starts <paramref name="flow"/> at <paramref name="step"/>, replacing any conversation in progress.</summary>
    public static Task StartAsync(this IConversation conversation, string flow, string step, CancellationToken token) =>
        conversation.SetAsync(new ConversationState(flow, step), token);

    /// <summary>Starts <paramref name="flow"/> at <paramref name="step"/> with <paramref name="data"/>, replacing any in progress.</summary>
    public static Task StartAsync<TData>(
        this IConversation conversation,
        string flow,
        string step,
        TData data,
        CancellationToken token
    ) => conversation.SetAsync(new ConversationState(flow, step, JsonSerializer.Serialize(data)), token);

    /// <summary>Moves the conversation to <paramref name="step"/>, keeping its data.</summary>
    /// <exception cref="InvalidOperationException">There is no active conversation.</exception>
    public static async Task MoveToAsync(this IConversation conversation, string step, CancellationToken token)
    {
        var active = await ActiveAsync(conversation, token);
        await conversation.SetAsync(active with { Step = step }, token);
    }

    /// <summary>Moves the conversation to <paramref name="step"/> with new <paramref name="data"/>.</summary>
    /// <exception cref="InvalidOperationException">There is no active conversation.</exception>
    public static async Task MoveToAsync<TData>(
        this IConversation conversation,
        string step,
        TData data,
        CancellationToken token
    )
    {
        var active = await ActiveAsync(conversation, token);
        await conversation.SetAsync(active with { Step = step, Data = JsonSerializer.Serialize(data) }, token);
    }

    /// <summary>The conversation's data, or <c>default</c> when there is none.</summary>
    public static async ValueTask<TData?> GetDataAsync<TData>(
        this IConversation conversation,
        CancellationToken token
    ) => await conversation.GetAsync(token) is { Data: { } data } ? JsonSerializer.Deserialize<TData>(data) : default;

    private static async Task<ConversationState> ActiveAsync(IConversation conversation, CancellationToken token) =>
        await conversation.GetAsync(token)
        ?? throw new InvalidOperationException("There is no active conversation to move.");
}
