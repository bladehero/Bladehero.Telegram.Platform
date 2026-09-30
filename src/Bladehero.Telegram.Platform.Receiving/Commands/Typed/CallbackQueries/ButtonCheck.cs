namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>What a button command makes of a tap: take it, leave it to another command, or refuse it.</summary>
/// <remarks><c>default</c> is <see cref="Accept"/>.</remarks>
public readonly record struct ButtonCheck
{
    private readonly Outcome _outcome;

    private ButtonCheck(Outcome outcome, string? answer, bool showAlert)
    {
        _outcome = outcome;
        Answer = answer;
        ShowAlert = showAlert;
    }

    /// <summary>The command takes the tap.</summary>
    public static ButtonCheck Accept { get; } = default;

    /// <summary>The tap is another command's.</summary>
    public static ButtonCheck Decline { get; } = new(Outcome.Decline, answer: null, showAlert: false);

    /// <summary>Whether the command takes the tap.</summary>
    public bool IsAccepted => _outcome is Outcome.Accept;

    /// <summary>Whether the tap is left to another command.</summary>
    public bool IsDeclined => _outcome is Outcome.Decline;

    /// <summary>Whether the tap is refused.</summary>
    public bool IsRejected => _outcome is Outcome.Reject;

    /// <summary>What a rejected tap is answered with; <c>null</c> answers it silently.</summary>
    public string? Answer { get; }

    /// <summary>Whether <see cref="Answer"/> is an alert rather than a notification.</summary>
    public bool ShowAlert { get; }

    /// <summary>Refuses the tap, answering it instead of handling it, e.g. on someone else's card.</summary>
    /// <param name="answer">The notification or alert, up to 200 characters; <c>null</c> answers silently.</param>
    /// <param name="showAlert">Whether to show <paramref name="answer"/> as an alert.</param>
    public static ButtonCheck Reject(string? answer = null, bool showAlert = false) =>
        new(Outcome.Reject, answer, showAlert);

    private enum Outcome
    {
        Accept,
        Decline,
        Reject,
    }
}
