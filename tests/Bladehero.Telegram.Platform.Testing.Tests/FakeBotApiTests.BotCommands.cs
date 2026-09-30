using FluentAssertions;

namespace Bladehero.Telegram.Platform.Testing.Tests;

public sealed partial class FakeBotApiTests
{
    private static readonly string LongestName = new('a', 64);

    // Expected: the marked commands, separated by |; empty for none.
    [Theory]
    [InlineData("/x@test_bot", "/x@test_bot")]
    [InlineData("/help.", "/help")]
    [InlineData("/start-x", "/start")]
    [InlineData("/start@x", "")]
    [InlineData("/start@xy now", "")]
    [InlineData("a/b", "")]
    [InlineData("x /cmd", "/cmd")]
    [InlineData("/a and /b", "/a|/b")]
    [InlineData("/cmd<", "")]
    [InlineData("/cmd>", "")]
    [InlineData("/cmd/x", "")]
    [InlineData("//cmd", "")]
    [InlineData("/кофе", "")]
    [InlineData("64", "64")]
    [InlineData("65", "")]
    public void BotCommandEntities_ShouldFollowTelegramsRules(string text, string marked)
    {
        // Arrange
        text = text switch
        {
            "64" => "/" + LongestName,
            "65" => "/" + LongestName + "a",
            _ => text,
        };
        marked = marked == "64" ? "/" + LongestName : marked;

        // Act
        var entities = FakeBotApi.BotCommandEntities(text);

        // Assert
        (entities ?? [])
            .Select(x => text.Substring(x!["offset"]!.GetValue<int>(), x["length"]!.GetValue<int>()))
            .Should()
            .Equal(marked.Length == 0 ? [] : marked.Split('|'));
    }
}
