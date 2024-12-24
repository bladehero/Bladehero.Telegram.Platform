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
        .ToDictionary(x => Enum.Parse<UpdateType>(x.Name), x => x);

    public virtual Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token) =>
        Task.FromResult(request.Update.Type == Type);

    public abstract Task HandleAsync(CommandRequest request, CancellationToken token = default);
}

public abstract class TypedTelegramCommand<T> : TypedTelegramCommand
    where T : class
{
    private T? _payload;

    public override Task<bool> CanHandleAsync(CommandRequest request, CancellationToken token)
    {
        _payload = (T)UpdateProperties[request.Update.Type].GetValue(request)!;
        return CanHandleAsync(request.Update.Id, _payload, token);
    }

    public sealed override Task HandleAsync(CommandRequest request, CancellationToken token = default)
    {
        if (_payload is null)
        {
            throw new InvalidOperationException(
                "Payload of update was not initialized properly through the CanHandleAsync method"
            );
        }

        return HandleAsync(request.Update.Id, _payload!, token);
    }

    protected abstract Task<bool> CanHandleAsync(int updateId, T payload, CancellationToken token = default);

    protected abstract Task HandleAsync(int updateId, T payload, CancellationToken token = default);
}
