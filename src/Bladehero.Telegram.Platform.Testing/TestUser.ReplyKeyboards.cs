using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

// Reply keyboards and ForceReply, as the user's app shows them.
public sealed partial class TestUser
{
    // What a button asks the app for instead of sending its text.
    private static readonly (string Field, string What)[] Requests =
    [
        ("request_contact", "a contact"),
        ("request_location", "a location"),
        ("request_users", "users"),
        ("request_chat", "a chat"),
        ("request_poll", "a poll"),
        ("web_app", "a web app"),
    ];

    /// <summary>The reply keyboard this user's app shows, or <c>null</c>.</summary>
    public TestReplyKeyboard? ReplyKeyboard =>
        _host.Api.ReplyKeyboardFor(Chat.Id, Id) is { } shown
            ? new TestReplyKeyboard(shown.Keyboard, shown.Hidden)
            : null;

    /// <summary>
    /// Presses a reply keyboard button, sending its label; in a group, as a reply to the keyboard's message.
    /// </summary>
    /// <returns>The message as posted.</returns>
    /// <exception cref="InvalidOperationException">
    /// The user has no reply keyboard, it has no such button, or the button asks for something the fake doesn't
    /// support.
    /// </exception>
    public async Task<TestMessage> PressesAsync(string label, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ThrowIfBlocked();

        var shown =
            _host.Api.ReplyKeyboardFor(Chat.Id, Id)
            ?? throw new InvalidOperationException($"{FirstName} has no reply keyboard to press.");
        var keyboard = new TestReplyKeyboard(shown.Keyboard, shown.Hidden);

        var button =
            keyboard.Button(label)
            ?? throw new InvalidOperationException(
                $"{FirstName} sees no \"{label}\" on the reply keyboard. The buttons are "
                    + $"\"{string.Join("\", \"", keyboard.Rows.SelectMany(row => row))}\"."
            );

        if (Requests.FirstOrDefault(request => button.ContainsKey(request.Field)) is { What: { } what })
        {
            throw new InvalidOperationException(
                $"The \"{label}\" button asks for {what}, which FakeBotApi doesn't support yet."
            );
        }

        _host.Api.Pressed(Chat.Id, Id, shown.MessageId);
        return await SendsTextAsync(label, Chat.IsGroup ? shown.MessageId : null, token);
    }

    // Text as the app sends it, marked as Telegram marks commands, optionally as a reply.
    private Task<TestMessage> SendsTextAsync(string text, int? replyTo, CancellationToken token)
    {
        var target = replyTo is { } messageId ? _host.Api.ReplyTarget(Chat.Id, messageId) : null;

        var preview = new JsonObject { ["text"] = text };
        if (target is not null)
        {
            preview["reply_to_message"] = target.DeepClone();
        }

        return DeliverAsync(
            preview,
            () =>
            {
                var content = new JsonObject { ["text"] = text };
                if (FakeBotApi.BotCommandEntities(text) is { } entities)
                {
                    content["entities"] = entities;
                }

                if (target is not null)
                {
                    content["reply_to_message"] = target.DeepClone();
                }

                return _host.Api.Receive(Chat.Id, Person, content);
            },
            token
        );
    }
}
