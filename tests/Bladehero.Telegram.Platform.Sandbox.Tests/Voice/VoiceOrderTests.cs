using Bladehero.Telegram.Platform.Sandbox.Voice;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;

namespace Bladehero.Telegram.Platform.Sandbox.Tests.Voice;

public sealed class VoiceOrderTests
{
    private static byte[] Speech => "a large one, please"u8.ToArray();

    [Fact]
    public async Task Voice_ShouldReachTheTranscriberAsSent()
    {
        // Arrange
        var transcriber = new ScriptedTranscriber();
        await using var bot = await StartAsync(transcriber);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsVoiceAsync(Speech);

        // Assert
        transcriber.Heard.Should().ContainSingle().Which.Should().Equal(Speech);
    }

    [Fact]
    public async Task Voice_NamingASize_ShouldStartTheOrderAtTheName()
    {
        // Arrange
        var transcriber = new ScriptedTranscriber { Transcription = Transcription.Of("A large one, please") };
        await using var bot = await StartAsync(transcriber);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsVoiceAsync(Speech);
        await nick.SendsAsync("Nicky");
        await nick.TapsAsync("Confirm");

        // Assert
        nick.Messages.Select(x => x.ToString())
            .Should()
            .Equal(
                "Nick: (voice 1s)",
                "Bot: «A large one, please»",
                "Bot: Size: Large ✓",
                "Bot: Whose name goes on the cup?",
                "Nick: Nicky",
                "Bot: Order placed ☕ — a Large coffee for Nicky."
            );
    }

    [Fact]
    public async Task Voice_WithoutASize_ShouldAskForIt()
    {
        // Arrange
        var transcriber = new ScriptedTranscriber { Transcription = Transcription.Of("A coffee, please") };
        await using var bot = await StartAsync(transcriber);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsVoiceAsync(Speech);

        // Assert
        nick.LastMessage.ToString().Should().Be("Bot: What size? [Small] [Medium] [Large] [Cancel]");
    }

    [Fact]
    public async Task Voice_WhenTranscriptionFails_ShouldSaySo()
    {
        // Arrange
        var transcriber = new ScriptedTranscriber { Transcription = Transcription.Failure("It's too noisy in here.") };
        await using var bot = await StartAsync(transcriber);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsVoiceAsync(Speech);

        // Assert
        nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: (voice 1s)", "Bot: It's too noisy in here.");
    }

    [Fact]
    public async Task Voice_WithoutATranscriberSetUp_ShouldSaySo()
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync();
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsVoiceAsync(Speech);

        // Assert
        nick.LastMessage.Text.Should().Be("Voice orders are not set up.");
    }

    private static Task<TelegramTestHost> StartAsync(ITranscriber transcriber) =>
        SandboxBot.StartAsync(configure: SandboxBot.Using(transcriber));
}
