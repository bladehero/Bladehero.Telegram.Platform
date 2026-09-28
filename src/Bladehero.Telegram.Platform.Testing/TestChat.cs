namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// A chat with the bot — private, or a group — as its members see it.
/// </summary>
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

    /// <summary>
    /// Every message in the chat, oldest first — the members' and the bot's — as they now stand: edits applied and
    /// deleted messages gone.
    /// </summary>
    public IReadOnlyList<TestMessage> Messages => [.. _host.Api.MessagesIn(Id).Select(json => new TestMessage(json))];

    /// <summary>The newest message in the chat, whoever sent it.</summary>
    /// <exception cref="InvalidOperationException">The chat has no messages.</exception>
    public TestMessage LastMessage =>
        Messages.LastOrDefault() ?? throw new InvalidOperationException($"There are no messages in {Description}.");

    internal string Description => _isGroup ? $"the {_name} group" : $"the chat with {_name}";

    /// <summary>
    /// <paramref name="firstName"/>, a member of this group. Asking for the same name again gives the same member.
    /// </summary>
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
