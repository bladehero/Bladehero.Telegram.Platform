using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests.Photos;

public sealed class PhotoEchoCommandTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Photo_ShouldComeBackAsTheSameFile(BotMode mode)
    {
        // Arrange
        var photo = "a photo of a cat"u8.ToArray();
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");

        // Act
        var sent = await nick.SendsPhotoAsync(photo);

        // Assert
        var echo = nick.LastMessage;
        var sendPhoto = bot.Api.Calls.Should().ContainSingle(x => x.Method == "sendPhoto").Subject;
        using (new AssertionScope())
        {
            echo.IsFromBot.Should().BeTrue();
            echo.Photo!.Content.Should().Equal(photo);
            echo.Caption.Should().Be("Nice photo! (16 bytes)");
            sendPhoto.Parameters["photo"]!.GetValue<string>().Should().Be(sent.Message.Photo![^1].FileId);
        }
    }
}
