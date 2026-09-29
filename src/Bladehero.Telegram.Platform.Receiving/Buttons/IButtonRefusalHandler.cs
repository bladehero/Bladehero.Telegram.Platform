namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>
/// Answers taps on <see cref="ButtonAttribute"/> buttons that no command took. The default says "That button is no
/// longer active." when the data no longer decodes, and answers silently otherwise.
/// </summary>
/// <remarks>
/// Register your own, in any order, to answer differently. Taps on hand-written data are never refused, and a custom
/// <c>ITelegramCommandExecutor</c> refuses none.
/// </remarks>
public interface IButtonRefusalHandler
{
    /// <summary>Answers a tap on a button whose prefix is registered but that no command took.</summary>
    Task HandleAsync(ButtonRefusal refusal, CancellationToken token);
}
