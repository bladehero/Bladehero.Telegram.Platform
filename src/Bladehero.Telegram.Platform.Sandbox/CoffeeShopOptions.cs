using Bladehero.Telegram.Platform.Sandbox.Loyalty;

namespace Bladehero.Telegram.Platform.Sandbox;

// The "CoffeeShop" configuration section.
internal sealed class CoffeeShopOptions
{
    public const string Section = "CoffeeShop";

    // Loyalty club members to start with, e.g. CoffeeShop:Members:0:UserId, :Name and :Points.
    public List<Member> Members { get; set; } = [];
}
