using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Background.Tests;

// The bot's identity straight from the client: every GetAsync is a getMe.
internal sealed class ClientIdentity(ITelegramBotClient client) : ITelegramBotIdentity
{
    public User? Current { get; private set; }

    public async ValueTask<User> GetAsync(CancellationToken token) => Current = await client.GetMe(token);
}
