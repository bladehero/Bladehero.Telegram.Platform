using Bladehero.Telegram.Platform.Receiving.Buttons;

namespace Bladehero.Telegram.Platform.Sandbox.Coffee;

// Coffee buttons are the shop's only bound ones, so "not yours" is about an order.
internal sealed class CoffeeShopRefusals : IButtonRefusalHandler
{
    public Task HandleAsync(ButtonRefusal refusal, CancellationToken token) =>
        refusal.AnswerAsync(
            refusal.Reason switch
            {
                ButtonRefusalReason.NotYours => "This order isn't yours.",
                ButtonRefusalReason.NoLongerActive => "That button is no longer active.",
                _ => null,
            },
            token: token
        );
}
