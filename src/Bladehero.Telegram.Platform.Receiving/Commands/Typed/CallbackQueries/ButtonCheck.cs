namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;

/// <summary>
/// What a button command makes of a tap it parsed: take it, leave it to another command, or refuse it.
/// </summary>
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

    /// <summary>The command takes the tap: <c>HandleAsync</c> runs.</summary>
    public static ButtonCheck Accept { get; } = default;

    /// <summary>
    /// The tap is another command's, as when <c>Parse</c> returns <c>null</c>: this command neither handles nor
    /// answers it.
    /// </summary>
    public static ButtonCheck Decline { get; } = new(Outcome.Decline, answer: null, showAlert: false);

    /// <summary>Whether the command takes the tap.</summary>
    public bool IsAccepted => _outcome is Outcome.Accept;

    /// <summary>Whether the tap is left to another command.</summary>
    public bool IsDeclined => _outcome is Outcome.Decline;

    /// <summary>Whether the tap is refused, answered by <c>RejectedAsync</c> instead of handled.</summary>
    public bool IsRejected => _outcome is Outcome.Reject;

    /// <summary>What a rejected tap is answered with; <c>null</c> answers it silently.</summary>
    public string? Answer { get; }

    /// <summary>Whether a rejected tap's <see cref="Answer"/> is an alert rather than a notification.</summary>
    public bool ShowAlert { get; }

    /// <summary>
    /// The tap is this command's, but refused, e.g. someone else's card: <c>RejectedAsync</c> runs instead of
    /// <c>HandleAsync</c>, answering with <paramref name="answer"/>, silently when <c>null</c>.
    /// </summary>
    /// <param name="answer">The notification or alert text, up to Telegram's 200 characters.</param>
    /// <param name="showAlert">Whether to show <paramref name="answer"/> as an alert the user must dismiss.</param>
    public static ButtonCheck Reject(string? answer = null, bool showAlert = false) =>
        new(Outcome.Reject, answer, showAlert);

    private enum Outcome
    {
        Accept,
        Decline,
        Reject,
    }
}
