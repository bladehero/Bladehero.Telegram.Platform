using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Commands.Execution;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bladehero.Telegram.Platform.Receiving.Commands.Typed;

/// <summary>
/// A command for a single update type; <see cref="TypedTelegramCommand{T}"/> and the bases built on it, such as
/// <see cref="Messages.MessageCommand"/>, add the typed payload.
/// </summary>
public abstract class TypedTelegramCommand : ITelegramCommand
{
    /// <summary>The update type this command handles; updates of any other type are declined.</summary>
    protected abstract UpdateType Type { get; }

    internal static readonly Dictionary<UpdateType, PropertyInfo> UpdateProperties = typeof(Update)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty)
        .GroupJoin(
            Enum.GetValues<UpdateType>(),
            x => x.Name,
            x => x.ToString(),
            (property, updateTypes) => new { Property = property, UpdateTypes = updateTypes }
        )
        .Where(x => x.UpdateTypes.Any())
        .ToDictionary(x => x.UpdateTypes.First(), x => x.Property);

    /// <inheritdoc/>
    public virtual Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
        Task.FromResult(request.Update.Type == Type);

    /// <inheritdoc/>
    public abstract Task HandleAsync(CommandRequest request, CancellationToken token);
}
