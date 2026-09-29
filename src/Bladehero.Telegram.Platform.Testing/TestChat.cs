namespace Bladehero.Telegram.Platform.Testing;

/// <summary>A private or group chat with the bot, as its members see it.</summary>
public sealed class TestChat
{
    private readonly TelegramTestHost _host;
    private readonly string _name;
    private readonly bool _isGroup;

    internal TestChat(TelegramTestHost host, long id, string name, bool isGroup)
    {
        _host = host;
        _name = name;
        _isGroup = isGroup;
        Id = id;
    }

    public long Id { get; }

    /// <summary>All messages, oldest first, with edits applied and deleted ones gone.</summary>
    public IReadOnlyList<TestMessage> Messages =>
        [.. _host.Api.MessagesIn(Id).Select(json => new TestMessage(json, _host.Api))];

    /// <summary>The newest message, whoever sent it.</summary>
    /// <exception cref="InvalidOperationException">The chat has no messages.</exception>
    public TestMessage LastMessage =>
        Messages.LastOrDefault() ?? throw new InvalidOperationException($"There are no messages in {Description}.");

    internal string Description => _isGroup ? $"the {_name} group" : $"the chat with {_name}";

    /// <summary><paramref name="firstName"/> as a member of this group.</summary>
    /// <exception cref="InvalidOperationException">This is a private chat.</exception>
    public TestUser Member(string firstName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);

        if (!_isGroup)
        {
            throw new InvalidOperationException(
                $"The chat with {_name} is private, and only a group has members. Start one with GroupChat."
            );
        }

        return new TestUser(_host, _host.Api.Person(firstName), this);
    }

    public override string ToString() => _name;
}
