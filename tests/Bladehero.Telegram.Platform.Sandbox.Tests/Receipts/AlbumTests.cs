using System.Text;
using Bladehero.Telegram.Platform.Sandbox.Receipts;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Receipts;

public sealed class AlbumTests
{
    [Fact]
    public async Task PhotoAlbum_ShouldBeAnnouncedOnceCountingItsPages()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAlbumAsync([Page(1), Page(2), Page(3)]);

        // Assert
        var prompt = nick.Messages.Where(x => x.IsFromBot).Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            prompt.ToString().Should().Be("Bot: Got 3 pages. [Read 3 pages]");
            prompt.IsEdited.Should().BeTrue();
            reader.Reads.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task PhotoAlbum_Read_ShouldPassEveryPageInOrderWithTheCaption()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAlbumAsync([Page(1), Page(2), Page(3)], caption: "Dinner for three");

        // Act
        await nick.TapsAsync("Read 3 pages");

        // Assert
        var read = reader.Reads.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            read.Pages.Select(Text).Should().Equal("page 1", "page 2", "page 3");
            read.Pages.Select(x => x.MediaType).Should().AllBe("image/jpeg");
            read.Caption.Should().Be("Dinner for three");
            PromptIn(nick).ToString().Should().Be("Bot: Receipt: 12.40 EUR → 12 points. [Add 12 points] [Discard]");
        }
    }

    [Fact]
    public async Task PdfAlbum_ShouldBeReadWithTheCaptionOfItsLastFile()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsDocumentAlbumAsync([(Page(1), "hotel-1.pdf"), (Page(2), "hotel-2.pdf")], caption: "Hotel Roma");

        // Act
        await nick.TapsAsync("Read 2 pages");

        // Assert
        var read = reader.Reads.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            read.Pages.Select(Text).Should().Equal("page 1", "page 2");
            read.Pages.Select(x => x.MediaType).Should().AllBe("application/pdf");
            read.Caption.Should().Be("Hotel Roma");
        }
    }

    [Fact]
    public async Task Album_ReadTwice_ShouldSayItIsNoLongerPending()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");
        await nick.SendsAlbumAsync([Page(1), Page(2)]);
        var prompt = PromptIn(nick);

        // Act
        var answers = await Task.WhenAll(
            nick.TapsAsync("Read 2 pages", on: prompt),
            nick.TapsAsync("Read 2 pages", on: prompt)
        );

        // Assert
        using (new AssertionScope())
        {
            answers
                .Select(x => x.ToString())
                .Should()
                .BeEquivalentTo(["Answered silently", "Notification: This receipt is no longer pending."]);
            reader.Reads.Should().ContainSingle();
        }
    }

    private static byte[] Page(int number) => Encoding.UTF8.GetBytes($"page {number}");

    private static string Text(ReceiptPage page) => Encoding.UTF8.GetString(page.Content);

    // The bot's one message, which follows the album's first page.
    private static TestMessage PromptIn(TestUser member) => member.Messages.Single(x => x.IsFromBot);

    private static Task<TelegramTestHost> StartAsync(IReceiptReader reader) =>
        SandboxBot.StartWithMembersAsync(SandboxBot.Using(reader), ("Nick", 0));
}
