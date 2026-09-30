using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands.Typed.Messages;

public sealed class MessageExtensionsTests
{
    [Theory]
    [InlineData("/last")]
    [InlineData("/LAST")]
    [InlineData("/last@FamilyBudgetBot")]
    [InlineData("/last 10")]
    [InlineData("/last@FamilyBudgetBot 10")]
    public void TheCommandIsRecognisedInEveryFormTelegramSendsIt(string text)
    {
        Assert.True(new Message { Text = text }.IsCommand("/last"));
    }

    [Theory]
    [InlineData("/lastly")]
    [InlineData("/other")]
    [InlineData("last")]
    [InlineData("tell me the /last one")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsNotTheCommand(string? text)
    {
        Assert.False(new Message { Text = text }.IsCommand("/last"));
    }

    [Theory]
    [InlineData("/last 10", "10")]
    [InlineData("/last@FamilyBudgetBot 10", "10")]
    [InlineData("/last   spaced  out ", "spaced  out")]
    public void ArgumentsAreTheTextAfterTheCommand(string text, string expected)
    {
        Assert.Equal(expected, new Message { Text = text }.ArgumentsOf("/last"));
    }

    [Theory]
    [InlineData("/rates\nall", true, "all")]
    [InlineData("/rates\tall", true, "all")]
    [InlineData("/rates@test_bot all", true, "all")]
    [InlineData("/ratesall", false, null)]
    [InlineData("/rates   ", true, null)]
    public void TheCommandEndsAtAnyWhitespace(string text, bool isCommand, string? arguments)
    {
        var message = new Message { Text = text };

        Assert.Equal(isCommand, message.IsCommand("/rates"));
        Assert.Equal(arguments, message.ArgumentsOf("/rates"));
    }

    [Theory]
    [InlineData("/last")]
    [InlineData("/last ")]
    [InlineData("/other 10")]
    public void WithoutArgumentsThereAreNone(string text)
    {
        Assert.Null(new Message { Text = text }.ArgumentsOf("/last"));
    }

    [Theory]
    [InlineData("/last")]
    [InlineData("/last@test_bot")]
    [InlineData("/last@Test_Bot 10")]
    public void InsideAnUpdateACommandAddressedToThisBotCountsIgnoringCase(string text)
    {
        Assert.True(InsideAnUpdateOf("test_bot", () => new Message { Text = text }.IsCommand("/last")));
    }

    [Fact]
    public void InsideAnUpdateACommandAddressedToAnotherBotDoesnt()
    {
        Assert.False(InsideAnUpdateOf("test_bot", () => new Message { Text = "/last@other_bot" }.IsCommand("/last")));
    }

    [Fact]
    public void OutsideAnUpdateAnyAddressedCommandCounts()
    {
        Assert.True(new Message { Text = "/last@other_bot" }.IsCommand("/last"));
    }

    [Theory]
    [InlineData("test_bot", true)]
    [InlineData("TEST_BOT", true)]
    [InlineData(null, true)]
    [InlineData("other_bot", false)]
    public void AnExplicitUsernameDecidesWhateverTheUpdate(string? botUsername, bool isCommand)
    {
        var message = new Message { Text = "/last@test_bot 10" };

        var (isIt, arguments) = InsideAnUpdateOf(
            "some_bot",
            () => (message.IsCommand("/last", botUsername), message.ArgumentsOf("/last", botUsername))
        );

        Assert.Equal(isCommand, isIt);
        Assert.Equal(isCommand ? "10" : null, arguments);
    }

    [Theory]
    [InlineData("/help.", null, "/help", ".")]
    [InlineData("/start-x", null, "/start", "-x")]
    [InlineData("/last10", 5, "/last", "10")]
    public void TheBotCommandEntityDecidesWhereTheCommandEnds(
        string text,
        int? entityLength,
        string command,
        string arguments
    )
    {
        var message = new Message
        {
            Text = text,
            Entities = entityLength is { } length
                ?
                [
                    new MessageEntity
                    {
                        Type = MessageEntityType.BotCommand,
                        Offset = 0,
                        Length = length,
                    },
                ]
                : null,
        };

        Assert.True(message.IsCommand(command));
        Assert.Equal(arguments, message.ArgumentsOf(command));
    }

    [Theory]
    [InlineData("/last@ab")]
    [InlineData("/last@a 10")]
    public void ANameOfFewerThan3CharactersAfterTheAtIsntACommand(string text)
    {
        Assert.False(new Message { Text = text }.IsCommand("/last"));
    }

    [Fact]
    public void ArgumentsOfACommandAddressedToAnotherBotAreNull()
    {
        Assert.Null(
            InsideAnUpdateOf("test_bot", () => new Message { Text = "/last@other_bot 10" }.ArgumentsOf("/last"))
        );
    }

    // As while the bot named username handles an update; the ambient name doesn't leak out.
    private static T InsideAnUpdateOf<T>(string username, Func<T> act) =>
        Task.Run(() =>
            {
                BotUsername.Current.Value = username;
                return act();
            })
            .GetAwaiter()
            .GetResult();
}
