using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>
/// How the bot answered a tap on one of its buttons: the notification Telegram shows over the chat, or an alert the
/// user has to dismiss.
/// </summary>
public sealed class TestCallbackAnswer
{
    internal TestCallbackAnswer(JsonObject? answer)
    {
        IsAnswered = answer is not null;
        Text = answer?["text"]?.GetValue<string>();
        IsAlert = answer?["show_alert"]?.GetValue<bool>() is true;
    }

    /// <summary>
    /// Whether the bot answered the tap at all. Until it does, the app shows the button as loading.
    /// </summary>
    public bool IsAnswered { get; }

    /// <summary>What the user is told, or <c>null</c> when the bot answered without a word.</summary>
    public string? Text { get; }

    /// <summary>Whether <see cref="Text"/> comes as an alert to dismiss rather than a passing notification.</summary>
    public bool IsAlert { get; }

    public override string ToString() =>
        !IsAnswered ? "No answer"
        : Text is null ? "Answered silently"
        : IsAlert ? $"Alert: {Text}"
        : $"Notification: {Text}";
}
