using System.Reflection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.DotNet.Platform.Receiving.Commands.Execution;

namespace Telegram.DotNet.Platform.Receiving.Commands.Typed;

public abstract class TypedTelegramCommand : ITelegramCommand
{
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

    public virtual Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
        Task.FromResult(request.Update.Type == Type);

    public abstract Task HandleAsync(CommandRequest request, CancellationToken token);
}
