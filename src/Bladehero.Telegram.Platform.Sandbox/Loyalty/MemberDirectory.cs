using Microsoft.Extensions.Options;

namespace Bladehero.Telegram.Platform.Sandbox.Loyalty;

// The loyalty club's members and their points: seeded from configuration (CoffeeShop:Members), then kept in memory.
internal sealed class MemberDirectory(IOptions<CoffeeShopOptions> options)
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Member> _members = options.Value.Members.ToDictionary(member => member.UserId);

    // False when they already are a member.
    public bool Join(long userId, string name)
    {
        lock (_gate)
        {
            return _members.TryAdd(userId, new Member(userId, name, Points: 0));
        }
    }

    public void Leave(long userId)
    {
        lock (_gate)
        {
            _members.Remove(userId);
        }
    }

    public Member? Find(long userId)
    {
        lock (_gate)
        {
            return _members.GetValueOrDefault(userId);
        }
    }

    // Null for someone who is not a member.
    public Member? Earn(long userId, int points)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(userId, out var member))
            {
                return null;
            }

            return _members[userId] = member with { Points = member.Points + points };
        }
    }

    // The member after spending `points`, or null when they do not have that many.
    public Member? Spend(long userId, int points)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(userId, out var member) || member.Points < points)
            {
                return null;
            }

            return _members[userId] = member with { Points = member.Points - points };
        }
    }
}
