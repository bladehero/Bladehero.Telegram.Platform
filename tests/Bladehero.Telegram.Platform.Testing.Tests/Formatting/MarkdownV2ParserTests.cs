using Bladehero.Telegram.Platform.Testing.Formatting;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Testing.Tests.Formatting;

public sealed class MarkdownV2ParserTests
{
    [Theory]
    [InlineData("Hello.", "Character '.' is reserved and must be escaped with the preceding '\\'")]
    [InlineData("a|b", "Character '|' is reserved and must be escaped with the preceding '\\'")]
    [InlineData("x *bold", "Can't find end of Bold entity at byte offset 2")]
    [InlineData("_it", "Can't find end of Italic entity at byte offset 0")]
    [InlineData("__u", "Can't find end of Underline entity at byte offset 0")]
    [InlineData("~s", "Can't find end of Strikethrough entity at byte offset 0")]
    [InlineData("||sp", "Can't find end of Spoiler entity at byte offset 0")]
    [InlineData("`c", "Can't find end of Code entity at byte offset 0")]
    [InlineData("```p", "Can't find end of Pre entity at byte offset 0")]
    [InlineData("```py\nx", "Can't find end of PreCode entity at byte offset 0")]
    [InlineData("[a", "Can't find end of TextUrl entity at byte offset 0")]
    [InlineData("![x", "Can't find end of CustomEmoji entity at byte offset 0")]
    [InlineData("[a](b", "Can't find end of a URL at byte offset 4")]
    [InlineData("![x]", "The entity must contain a tg://emoji or tg://time URL")]
    [InlineData("![x](tg://foo)", "Invalid tg://emoji or tg://time URL specified")]
    public void Parse_ShouldRefuseLikeTelegram(string markdown, string message)
    {
        // Act
        var act = () => MarkdownV2Parser.Parse(markdown);

        // Assert
        act.Should().Throw<FormattingException>().Which.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("+5")]
    public void Parse_ACustomEmojiIdWithASign_ShouldBeRefused(string id)
    {
        // Act
        var act = () => MarkdownV2Parser.Parse($"![👍](tg://emoji?id={id})");

        // Assert
        act.Should()
            .Throw<FormattingException>()
            .Which.Message.Should()
            .Be("Invalid tg://emoji or tg://time URL specified");
    }

    [Fact]
    public void Parse_ShouldProduceEveryEntity()
    {
        // Act
        var (text, entities) = MarkdownV2Parser.Parse(
            "*b* _i_ __u__ ~s~ ||p|| `c` ```py\nx``` [l](https://e.com) ![👍](tg://emoji?id=5368324170671202286)\n>q"
        );

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("b i u s p c x l 👍\nq");
            HtmlParserTests
                .Describe(entities)
                .Should()
                .Equal(
                    "bold 0+1",
                    "italic 2+1",
                    "underline 4+1",
                    "strikethrough 6+1",
                    "spoiler 8+1",
                    "code 10+1",
                    "pre 12+1 py",
                    "text_link 14+1 https://e.com",
                    "custom_emoji 16+2 5368324170671202286",
                    "blockquote 19+1"
                );
        }
    }

    [Fact]
    public void Parse_EscapedCharacters_ShouldBeLiteral()
    {
        // Act
        var (text, entities) = MarkdownV2Parser.Parse("1\\.5 \\*not bold\\* \\_ \\\\");

        // Assert
        using (new AssertionScope())
        {
            text.Should().Be("1.5 *not bold* _ \\");
            entities.Should().BeEmpty();
        }
    }
}
