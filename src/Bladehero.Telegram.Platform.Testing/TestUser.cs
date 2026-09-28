using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A person in a <see cref="TestChat"/>, doing what a user does in the Telegram app: typing messages and tapping the
/// bot's buttons. Each action returns once the bot has finished handling it, and rethrows whatever a command threw.
/// </summary>
public sealed class TestUser
{
    private readonly TelegramTestHost _host;
    private readonly JsonObject _person;

    internal TestUser(TelegramTestHost host, JsonObject person, TestChat chat)
    {
        _host = host;
        _person = person;
        Chat = chat;
    }

    /// <summary>The person's Telegram user id — the same in every chat they are in.</summary>
    public long Id => _person["id"]!.GetValue<long>();

    public string FirstName => _person["first_name"]!.GetValue<string>();

    public TestChat Chat { get; }

    /// <inheritdoc cref="TestChat.Messages"/>
    public IReadOnlyList<TestMessage> Messages => Chat.Messages;

    /// <inheritdoc cref="TestChat.LastMessage"/>
    public TestMessage LastMessage => Chat.LastMessage;

    /// <summary>Sends <paramref name="text"/> to the chat.</summary>
    public Task SendsAsync(string text, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        var message = _host.Api.Receive(Chat.Id, _person, text);
        return _host.DeliverAsync(new JsonObject { ["message"] = message }, token);
    }

    /// <summary>
    /// Taps the inline button labelled <paramref name="button"/> on the newest message showing it, and the bot
    /// receives the button's callback query.
    /// </summary>
    /// <param name="button">The button's text, exactly as the bot sent it.</param>
    /// <param name="on">The message to tap the button on, when an older message shows the same button.</param>
    /// <param name="token">Stops waiting for the bot.</param>
    /// <returns>How the bot answered the tap: the notification or alert the user sees, if any.</returns>
    /// <exception cref="InvalidOperationException">
    /// No message shows the button, the message shows it twice, or the button sends the bot nothing — a link, say.
    /// </exception>
    public async Task<TestCallbackAnswer> TapsAsync(
        string button,
        TestMessage? on = null,
        CancellationToken token = default
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(button);

        var (message, data) = Find(button, on);
        var queryId = _host.Api.NextCallbackQueryId();
        var query = new JsonObject
        {
            ["id"] = queryId,
            ["from"] = _person.DeepClone(),
            ["message"] = message.ToJson(),
            ["chat_instance"] = Chat.Id.ToString(),
            ["data"] = data,
        };

        await _host.DeliverAsync(new JsonObject { ["callback_query"] = query }, token);
        return new TestCallbackAnswer(_host.Api.CallbackAnswer(queryId));
    }

    public override string ToString() => FirstName;

    private (TestMessage Message, string Data) Find(string button, TestMessage? on)
    {
        var messages = Messages;
        IEnumerable<TestMessage> candidates = on is null ? messages.Reverse() : [Current(on, messages)];

        foreach (var message in candidates)
        {
            switch (message.Keyboard.Where(x => x.Text == button).ToArray())
            {
                case []:
                    continue;
                case [{ CallbackData: { } data }]:
                    return (message, data);
                case [_]:
                    throw new InvalidOperationException(
                        $"The \"{button}\" button is not a callback button: the Telegram app handles it, and the bot "
                            + "never hears of the tap."
                    );
                default:
                    throw new InvalidOperationException(
                        $"\"{message.Text}\" shows more than one \"{button}\" button, so which one {FirstName} taps "
                            + "is ambiguous."
                    );
            }
        }

        var shown = candidates.SelectMany(message => message.Buttons).Distinct().ToArray();
        var where = on is null ? $"in {Chat.Description}" : $"on \"{on.Text}\"";
        throw new InvalidOperationException(
            $"{FirstName} sees no \"{button}\" button {where}. "
                + (shown.Length == 0 ? "There are no buttons." : $"The buttons are \"{string.Join("\", \"", shown)}\".")
        );
    }

    private TestMessage Current(TestMessage on, IReadOnlyList<TestMessage> messages)
    {
        if (on.Message.Chat.Id != Chat.Id)
        {
            throw new InvalidOperationException($"\"{on.Text}\" is not in {Chat.Description}.");
        }

        return messages.FirstOrDefault(message => message.Id == on.Id)
            ?? throw new InvalidOperationException($"\"{on.Text}\" is no longer in {Chat.Description}.");
    }
}
