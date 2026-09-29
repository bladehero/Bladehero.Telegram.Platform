using Bladehero.Telegram.Platform.Sandbox.Receipts;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Receipts;

public sealed class ReceiptTests
{
    private const string TooBig = "That file is too big — send a photo of the receipt instead.";

    private static byte[] Receipt => "a receipt from Luigi's"u8.ToArray();

    [Fact]
    public async Task Photo_ShouldBeReadOnItsLargestSize()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync(Receipt);

        // Assert
        var page = reader.Reads.Should().ContainSingle().Which.Pages.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            page.Content.Should().Equal(Receipt);
            page.MediaType.Should().Be("image/jpeg");
            nick.LastMessage.ToString().Should().Be("Bot: Receipt: 12.40 EUR → 12 points. [Add 12 points] [Discard]");
        }
    }

    [Fact]
    public async Task Photo_WithACaption_ShouldPassItToTheReader()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync(Receipt, caption: "Lunch with Anna");

        // Assert
        reader.Reads.Should().ContainSingle().Which.Caption.Should().Be("Lunch with Anna");
    }

    [Fact]
    public async Task PdfDocument_ShouldBeRead()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync(Receipt, "receipt.pdf");

        // Assert
        var page = reader.Reads.Should().ContainSingle().Which.Pages.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            page.Content.Should().Equal(Receipt);
            page.MediaType.Should().Be("application/pdf");
            nick.LastMessage.Text.Should().Be("Receipt: 12.40 EUR → 12 points.");
        }
    }

    [Fact]
    public async Task ZipDocument_ShouldBeRefusedWithoutReading()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync(Receipt, "receipts.zip");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be("Send a photo or a PDF of the receipt.");
            reader.Reads.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Document_Over5Megabytes_ShouldSayTooBigWithoutReading()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync(new byte[5 * 1024 * 1024 + 1], "scan.pdf");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be(TooBig);
            reader.Reads.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Document_Over20Megabytes_ShouldSayTooBig()
    {
        // Arrange: Telegram refuses to let a bot download it at all.
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsDocumentAsync(new byte[20 * 1024 * 1024 + 1], "scan.pdf");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be(TooBig);
            reader.Reads.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Receipt_WhenTheReaderFails_ShouldShowItsError()
    {
        // Arrange
        var reader = new ScriptedReceiptReader { Reading = ReceiptReading.Failure("I can't make out the total.") };
        await using var bot = await StartAsync(reader);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync(Receipt);

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: (photo)", "Bot: I can't make out the total.");
    }

    [Fact]
    public async Task Receipt_WithoutAReaderSetUp_ShouldSaySo()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 0));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsPhotoAsync(Receipt);

        // Assert
        nick.LastMessage.Text.Should().Be("Receipt reading is not set up.");
    }

    [Fact]
    public async Task Receipt_FromAStranger_ShouldBeIgnored()
    {
        // Arrange
        var reader = new ScriptedReceiptReader();
        await using var bot = await StartAsync(reader);
        var anna = bot.PrivateChat("Anna");

        // Act
        await anna.SendsPhotoAsync(Receipt);

        // Assert
        using (new AssertionScope())
        {
            anna.Messages.Select(x => x.ToString()).Should().Equal("Anna: (photo)");
            reader.Reads.Should().BeEmpty();
        }
    }

    private static Task<TelegramTestHost> StartAsync(IReceiptReader reader) =>
        SandboxBot.StartWithMembersAsync(SandboxBot.Using(reader), ("Nick", 0));
}
