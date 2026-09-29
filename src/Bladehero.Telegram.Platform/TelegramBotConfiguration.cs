namespace Bladehero.Telegram.Platform;

/// <summary>The bot's settings for <c>AddTelegramBot</c>; the receiving configurations extend them.</summary>
public class TelegramBotConfiguration
{
    /// <summary>The token @BotFather gave the bot, such as <c>123456:ABC-DEF...</c>. Required.</summary>
    public required string Token { get; set; }
}
