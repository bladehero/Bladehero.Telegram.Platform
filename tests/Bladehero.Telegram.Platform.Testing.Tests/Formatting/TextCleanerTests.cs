using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Testing.Formatting;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests.Formatting;

public sealed class TextCleanerTests
{
    [Fact]
    public void Clean_ShouldDeleteCarriageReturns()
    {
        // Act
        var (text, entities) = TextCleaner.Clean("x\r\ny", [Bold(0, 4)]);

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("x\ny");
            HtmlParserTests.Describe(entities).Should().Equal("bold 0+3");
        }
    }

    [Fact]
    public void Clean_ShouldDeleteLineAndParagraphSeparators()
    {
        // Arrange: U+2028 to U+202E go, the direction marks among them.
        var raw = new string(['a', (char)0x2028, 'b', (char)0x2029, (char)0x202E, 'c']);

        // Act
        var (text, entities) = TextCleaner.Clean(raw, [Bold(2, 4)]);

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("abc");
            HtmlParserTests.Describe(entities).Should().Equal("bold 1+2");
        }
    }

    [Fact]
    public void Clean_ShouldTurnOtherControlCharactersIntoSpaces()
    {
        // Act
        var (text, _) = TextCleaner.Clean(new string(['a', '\t', 'b', (char)1, 'c', '\n', 'd']), []);

        // Assert
        text.Should().Be("a b c\nd");
    }

    [Fact]
    public void Trim_ShouldKeepLeadingSpacesInsideAnEntity()
    {
        // Act: <b>  x</b>
        var (text, entities) = TextCleaner.Trim("  x", [Bold(0, 3)]);

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("  x");
            HtmlParserTests.Describe(entities).Should().Equal("bold 0+3");
        }
    }

    [Fact]
    public void Trim_ShouldKeepANonBreakingSpace()
    {
        // Arrange: NBSP and U+3000 around the text.
        var raw = new string([(char)0xA0, 'x', (char)0x3000]);

        // Act
        var (text, _) = TextCleaner.Trim($" \n{raw}\n ", []);

        // Assert
        text.Should().Be(raw);
    }

    [Fact]
    public void Trim_ShouldClipEntitiesAtTheEnd()
    {
        // Act
        var (text, entities) = TextCleaner.Trim("  x  \n", [Bold(2, 4), Bold(4, 2)]);

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("x");
            HtmlParserTests.Describe(entities).Should().Equal("bold 0+1");
        }
    }

    private static JsonObject Bold(int offset, int length) => Markup.Entity("bold", offset, length);
}
