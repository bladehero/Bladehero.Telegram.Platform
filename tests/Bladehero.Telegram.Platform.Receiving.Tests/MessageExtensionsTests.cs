using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests;

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
    [InlineData("/last")]
    [InlineData("/last ")]
    [InlineData("/other 10")]
    public void WithoutArgumentsThereAreNone(string text)
    {
        Assert.Null(new Message { Text = text }.ArgumentsOf("/last"));
    }
}
