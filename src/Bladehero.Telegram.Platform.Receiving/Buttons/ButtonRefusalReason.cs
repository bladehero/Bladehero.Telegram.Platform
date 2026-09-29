namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Why no command took a tap on a <see cref="ButtonDataAttribute"/> button.</summary>
public enum ButtonRefusalReason
{
    /// <summary>
    /// The data no longer decodes, its conversation run is over or replaced, or no step of the run takes it, e.g. a
    /// button sent before its type changed.
    /// </summary>
    NoLongerActive,

    /// <summary>The data decodes, but every command declined it, e.g. a stranger's tap.</summary>
    Unclaimed,

    /// <summary>A bound button tapped by someone other than the user it was shown for.</summary>
    NotYours,
}
