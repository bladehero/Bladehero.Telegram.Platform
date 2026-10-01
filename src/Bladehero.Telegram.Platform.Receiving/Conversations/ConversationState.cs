using System.Security.Cryptography;

namespace Bladehero.Telegram.Platform.Receiving.Conversations;

/// <summary>Where a conversation stands.</summary>
/// <param name="Flow">Matched against <see cref="ConversationStepAttribute.Flow"/>.</param>
/// <param name="Step">Matched against <see cref="ConversationStepAttribute.Step"/>.</param>
/// <param name="Data">
/// Data carried between steps; JSON via <see cref="ConversationExtensions"/>, opaque to the store.
/// </param>
public sealed record ConversationState(string Flow, string Step, string? Data = null)
{
    private const string IdLetters = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>This run of the flow, set once a button is bound to it; <c>StartAsync</c> starts a new run.</summary>
    /// <remarks>A store must keep it, or bound buttons stop working after a reload.</remarks>
    public string? Id { get; init; }

    /// <summary>A new run id, for a state saved outside an update, e.g. by a background job.</summary>
    public static string NewId() => RandomNumberGenerator.GetString(IdLetters, 8);
}
