using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

internal sealed class Conversation(IConversationStore store) : IConversation
{
    private ConversationKey? _key;
    private ConversationState? _state;
    private bool _loaded;

    internal void Bind(Update update)
    {
        _key = ConversationKey.For(update);
        _state = null;
        _loaded = false;
    }

    public async ValueTask<ConversationState?> GetAsync(CancellationToken token)
    {
        if (!_loaded && _key is { } key)
        {
            _state = await store.GetAsync(key, token);
        }

        _loaded = true;
        return _state;
    }

    public async Task SetAsync(ConversationState state, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state);

        var key =
            _key
            ?? throw new InvalidOperationException("This update has no user in a chat to hold a conversation with.");

        await store.SaveAsync(key, state, token);
        _state = state;
        _loaded = true;
    }

    public async Task EndAsync(CancellationToken token)
    {
        if (_key is { } key)
        {
            await store.RemoveAsync(key, token);
        }

        _state = null;
        _loaded = true;
    }
}
