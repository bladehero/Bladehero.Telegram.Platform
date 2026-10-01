using Bladehero.Telegram.Platform.Testing.Formatting;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests.Formatting;

public sealed class MarkdownParserTests
{
    [Theory]
    [InlineData("*bold", "Can't find end of the entity starting at byte offset 0")]
    [InlineData("a _it", "Can't find end of the entity starting at byte offset 2")]
    public void Parse_AnUnclosedEntity_ShouldBeRefused(string markdown, string message)
    {
        // Act
        var act = () => MarkdownParser.Parse(markdown);

        // Assert
        act.Should().Throw<FormattingException>().Which.Message.Should().Be(message);
    }

    [Fact]
    public void Parse_ShouldProduceBoldItalicCodeAndLinks()
    {
        // Act
        var (text, entities) = MarkdownParser.Parse("*b* _i_ `c` ```py\nx``` [l](https://e.com) \\*");

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("b i c x l *");
            HtmlParserTests
                .Describe(entities)
                .Should()
                .Equal("bold 0+1", "italic 2+1", "code 4+1", "pre 6+1 py", "text_link 8+1 https://e.com");
        }
    }
}
