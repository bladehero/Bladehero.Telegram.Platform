using System.Text.Json.Nodes;
using Bladehero.Telegram.Platform.Testing.Formatting;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests.Formatting;

public sealed class HtmlParserTests
{
    [Theory]
    [InlineData("<br>", "Unsupported start tag \"br\" at byte offset 0")]
    [InlineData("a < b", "Unsupported start tag \"\" at byte offset 2")]
    [InlineData("x <b", "Unclosed start tag at byte offset 2")]
    [InlineData("<a href", "Unclosed start tag \"a\" at byte offset 0")]
    [InlineData("<a href=\"x\"", "Unclosed start tag at byte offset 0")]
    [InlineData("<a =\"x\">y</a>", "Empty attribute name in the tag \"a\" at byte offset 0")]
    [InlineData("<a href=x/>y</a>", "Unexpected end of name token at byte offset 8")]
    [InlineData("<span>x</span>", "Tag \"span\" must have class \"tg-spoiler\" at byte offset 0")]
    [InlineData("x</b>", "Unexpected end tag at byte offset 1")]
    [InlineData("<b>x</b", "Unclosed end tag at byte offset 4")]
    [InlineData("<b>x</i>", "Unmatched end tag at byte offset 4, expected \"</b>\", found \"</i>\"")]
    [InlineData("<b>x", "Can't find end tag corresponding to start tag \"b\"")]
    [InlineData("<tg-emoji emoji-id=\"x\">y</tg-emoji>", "Invalid custom emoji identifier specified")]
    public void Parse_ShouldRefuseLikeTelegram(string html, string message)
    {
        // Act
        var act = () => HtmlParser.Parse(html);

        // Assert
        act.Should().Throw<FormattingException>().Which.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("+5")]
    public void Parse_ACustomEmojiIdWithASign_ShouldBeRefused(string id)
    {
        // Act
        var act = () => HtmlParser.Parse($"<tg-emoji emoji-id=\"{id}\">👍</tg-emoji>");

        // Assert
        act.Should()
            .Throw<FormattingException>()
            .Which.Message.Should()
            .Be("Invalid custom emoji identifier specified");
    }

    [Fact]
    public void Parse_AnOffsetAfterCyrillic_ShouldCountBytes()
    {
        // Act: six Cyrillic letters are 12 bytes, and the space one more.
        var act = () => HtmlParser.Parse("привет <br>");

        // Assert
        act.Should().Throw<FormattingException>().WithMessage("Unsupported start tag \"br\" at byte offset 13");
    }

    [Theory]
    [InlineData("Tom & Jerry")]
    [InlineData("&nbsp; and &copy")]
    [InlineData("a > b")]
    public void Parse_ShouldKeepAnUnknownEntityOrABareAmpersand(string html)
    {
        // Act
        var (text, entities) = HtmlParser.Parse(html);

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be(html);
            entities.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("&lt;&gt;&amp;&quot;", "<>&\"")]
    [InlineData("&lt&gt", "<>")]
    [InlineData("&#65;&#x42;&#67", "ABC")]
    public void Parse_ShouldDecodeTheKnownEntitiesWithOrWithoutSemicolon(string html, string expected)
    {
        // Act
        var (text, _) = HtmlParser.Parse(html);

        // Assert
        text.Should().Be(expected);
    }

    [Fact]
    public void Parse_ShouldProduceNestedEntities()
    {
        // Act
        var (text, entities) = HtmlParser.Parse("<b>a<i>b</i></b>");

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("ab");
            Describe(entities).Should().Equal("bold 0+2", "italic 1+1");
        }
    }

    [Fact]
    public void Parse_ShouldReadPreWithALanguage()
    {
        // Act
        var (text, entities) = HtmlParser.Parse(
            "<pre><code class=\"language-python\">x = 1</code></pre>\n<code class=\"language-go\">y</code>"
        );

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("x = 1\ny");
            Describe(entities).Should().Equal("pre 0+5 python", "code 6+1");
        }
    }

    [Fact]
    public void Parse_ShouldReadALinkAndBlockquotes()
    {
        // Act
        var (text, entities) = HtmlParser.Parse(
            "<a href=\"https://example.com\">site</a> <blockquote>q</blockquote><blockquote expandable>e</blockquote>"
                + "<tg-spoiler>s</tg-spoiler><tg-emoji emoji-id=\"5368324170671202286\">👍</tg-emoji>"
        );

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("site qes👍");
            Describe(entities)
                .Should()
                .Equal(
                    "text_link 0+4 https://example.com",
                    "blockquote 5+1",
                    "expandable_blockquote 6+1",
                    "spoiler 7+1",
                    "custom_emoji 8+2 5368324170671202286"
                );
        }
    }

    [Fact]
    public void Parse_AnEmojiEntity_ShouldHaveUtf16Length2()
    {
        // Act
        var (_, entities) = HtmlParser.Parse("<b>😀</b>");

        // Assert
        Describe(entities).Should().Equal("bold 0+2");
    }

    // "bold 0+2", with the entity's url, language or custom emoji id after it.
    internal static IEnumerable<string> Describe(IEnumerable<JsonObject> entities) =>
        entities.Select(entity =>
            string.Join(
                " ",
                new[]
                {
                    $"{entity["type"]} {entity["offset"]}+{entity["length"]}",
                    entity["url"]?.GetValue<string>(),
                    entity["language"]?.GetValue<string>(),
                    entity["custom_emoji_id"]?.GetValue<string>(),
                }.OfType<string>()
            )
        );
}
