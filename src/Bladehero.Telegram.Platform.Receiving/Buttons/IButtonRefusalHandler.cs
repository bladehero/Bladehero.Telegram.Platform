namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Answers taps on typed buttons that no command took.</summary>
/// <remarks>
/// The default says "That button isn't yours." for someone else's bound button, "That button is no longer active."
/// when the data no longer decodes or its run is over, and answers silently otherwise. Register your own, in any
/// order, to replace it.
/// </remarks>
public interface IButtonRefusalHandler
{
    /// <summary>Answers the refused tap.</summary>
    Task HandleAsync(ButtonRefusal refusal, CancellationToken token);
}
