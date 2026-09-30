namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Why no command took a tap on a <see cref="ButtonDataAttribute"/> button.</summary>
public enum ButtonRefusalReason
{
    /// <summary>The data no longer decodes, e.g. a button sent before its type changed.</summary>
    NoLongerActive,

    /// <summary>The data decodes, but every command declined it, e.g. a stranger's tap.</summary>
    Unclaimed,
}
