using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>How the bot answered a button tap: a notification, an alert, or nothing.</summary>
public sealed class TestCallbackAnswer
{
    internal TestCallbackAnswer(JsonObject? answer)
    {
        IsAnswered = answer is not null;
        Text = answer?["text"]?.GetValue<string>();
        IsAlert = answer?["show_alert"]?.GetValue<bool>() is true;
    }

    /// <summary>Whether the bot answered at all (until then the app shows a spinner).</summary>
    public bool IsAnswered { get; }

    /// <summary>The text shown, or <c>null</c> for a silent answer.</summary>
    public string? Text { get; }

    /// <summary>Whether <see cref="Text"/> is an alert rather than a notification.</summary>
    public bool IsAlert { get; }

    public override string ToString() =>
        !IsAnswered ? "No answer"
        : Text is null ? "Answered silently"
        : IsAlert ? $"Alert: {Text}"
        : $"Notification: {Text}";
}
