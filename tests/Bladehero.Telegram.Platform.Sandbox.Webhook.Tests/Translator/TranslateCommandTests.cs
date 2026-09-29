using Bladehero.Telegram.Platform.Sandbox.Webhook.Translator;
using Bladehero.Telegram.Platform.Testing;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bladehero.Telegram.Platform.Sandbox.Webhook.Tests.Translator;

public sealed class TranslateCommandTests
{
    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Translate_ShouldReplyWithTheTranslation(BotMode mode)
    {
        // Arrange
        var translator = new ScriptedTranslator { Translation = Translation.Of("hola") };
        await using var bot = await StartAsync(mode, translator);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/translate hello");

        // Assert
        using (new AssertionScope())
        {
            translator.Texts.Should().Equal("hello");
            nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /translate hello", "Bot: hola");
        }
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Translate_WithoutText_ShouldAskWhat(BotMode mode)
    {
        // Arrange
        var translator = new ScriptedTranslator();
        await using var bot = await StartAsync(mode, translator);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/translate");

        // Assert
        using (new AssertionScope())
        {
            nick.LastMessage.Text.Should().Be("What should I translate? Try /translate hello");
            translator.Texts.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Translate_WithoutATranslatorSetUp_ShouldSaySo(BotMode mode)
    {
        // Arrange
        await using var bot = await SandboxBot.StartAsync(mode);
        var nick = bot.PrivateChat("Nick");

        // Act
        await nick.SendsAsync("/translate hello");

        // Assert
        nick.LastMessage.Text.Should().Be("Translation is not set up.");
    }

    [Theory]
    [InlineData(BotMode.Webhook)]
    [InlineData(BotMode.LongPolling)]
    public async Task Translate_WhenTheTranslatorThrows_ShouldFailTheAction(BotMode mode)
    {
        // Arrange
        var translator = new ScriptedTranslator { Failure = new HttpRequestException("The translator is down.") };
        await using var bot = await StartAsync(mode, translator);
        var nick = bot.PrivateChat("Nick");

        // Act
        var error = await Record.ExceptionAsync(() => nick.SendsAsync("/translate hello"));

        // Assert
        using (new AssertionScope())
        {
            error.Should().BeOfType<HttpRequestException>().Which.Message.Should().Be("The translator is down.");
            nick.Messages.Select(x => x.ToString()).Should().Equal("Nick: /translate hello");
        }
    }

    // The app registered its disabled translator itself, so the stand-in replaces it.
    private static Task<TelegramTestHost> StartAsync(BotMode mode, ITranslator translator) =>
        SandboxBot.StartAsync(
            mode,
            configure: web =>
                web.ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<ITranslator>(translator)))
        );
}
