namespace Bladehero.Telegram.Platform.Receiving.Buttons;

/// <summary>Why no command took a tap on a <see cref="ButtonDataAttribute"/> button.</summary>
public enum ButtonRefusalReason
{
    /// <summary>The data no longer decodes, or its conversation run is over or none of its steps takes it.</summary>
    NoLongerActive,

    /// <summary>The data decodes, but every command declined it, e.g. a stranger's tap.</summary>
    Unclaimed,

    /// <summary>A bound button tapped by someone other than the user it was shown for.</summary>
    NotYours,
}
