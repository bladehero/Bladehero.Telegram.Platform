using Microsoft.Extensions.Logging;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>A log entry the bot wrote, with the update it was handling, if any.</summary>
/// <param name="Level">The entry's level.</param>
/// <param name="Category">The logger's category, usually the type that logged.</param>
/// <param name="EventId">The entry's event id.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception logged with it, if any.</param>
/// <param name="UpdateId">The update being handled, or <c>null</c> outside any update.</param>
public sealed record TestLog(
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    Exception? Exception,
    long? UpdateId
)
{
    /// <summary>"Error [Category] Message", with the exception type when there is one.</summary>
    public override string ToString() =>
        Exception is null
            ? $"{Level} [{Category}] {Message}"
            : $"{Level} [{Category}] {Message} ({Exception.GetType().Name})";
}
