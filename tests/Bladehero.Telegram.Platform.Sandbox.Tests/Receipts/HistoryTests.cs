using System.Globalization;
using Bladehero.Telegram.Platform.Sandbox.Receipts;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Receipts;

public sealed class HistoryTests
{
    [Fact]
    public async Task History_ShouldSendTheReceiptsAsACsvDocument()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(
            SandboxBot.Using<IReceiptReader>(new ScriptedReceiptReader()),
            ("Nick", 0)
        );
        var nick = bot.PrivateChat("Nick");
        var receipt = await nick.SendsPhotoAsync("a receipt from Luigi's"u8.ToArray());
        await nick.TapsAsync("Add 12 points");

        // Act
        await nick.SendsAsync("/history");

        // Assert
        var sent = nick.LastMessage;
        var csv = sent.Document!;
        using (new AssertionScope())
        {
            csv.FileName.Should().Be("receipts.csv");
            csv.MimeType.Should().Be("text/csv");
            csv.ReadAsString()
                .Should()
                .Be(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"date,total,points\n{receipt.Message.Date:yyyy-MM-dd},12.40,12\n"
                    )
                );
            sent.Caption.Should().Be("Your receipts");
        }
    }

    [Fact]
    public async Task History_WithNoReceipts_ShouldSaySo()
    {
        // Arrange
        await using var bot = await SandboxBot.StartWithMembersAsync(("Nick", 0));
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/history");

        // Assert
        nick.LastMessage.Text.Should().Be("No receipts yet.");
    }
}
