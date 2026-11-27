using System.Security.Cryptography;
using System.Text;

namespace Bladehero.Telegram.Platform.Hub;

/// <summary>The names a hub pass is checked and read by.</summary>
public static class HubPassDefaults
{
    /// <summary>The authentication scheme <c>AddHubPasses</c> adds.</summary>
    public const string Scheme = "HubPass";

    /// <summary>One claim per scope the pass carries, as a key's principal has them.</summary>
    public const string ScopeClaim = "scope";

    /// <summary>The caller's fixed id, a UUID: see <see cref="ClientIdOf"/>.</summary>
    public const string ClientIdClaim = "client_id";

    /// <summary>The caller's name for people: its title in the hub, or its slug.</summary>
    public const string ClientNameClaim = "client_name";

    /// <summary>The caller's slug, such as <c>income</c>: present only on a principal made from a pass.</summary>
    public const string HubAppClaim = "hub_app";

    /// <summary>
    /// The person a person pass acted for, by Telegram id: present only on a principal made from one. See
    /// <c>ClaimsPrincipal.HubPerson()</c>.
    /// </summary>
    public const string HubPersonClaim = "hub_person";

    /// <summary>The delegate a person pass came through, such as <c>home</c>: present only beside <see cref="HubPersonClaim"/>.</summary>
    public const string HubViaClaim = "hub_via";

    /// <summary>
    /// The id this app gives a person's connection at a delegate: <see cref="ClientIdOf"/> of <c>&lt;via&gt;:&lt;connection
    /// id&gt;</c>, so Home's connection <c>8b1e…</c> is <c>ClientIdOf("home:8b1e…")</c> everywhere.
    /// </summary>
    /// <param name="via">The delegate's slug.</param>
    /// <param name="connection">The connection's id at the delegate.</param>
    public static Guid ConnectionIdOf(string via, Guid connection) => ClientIdOf($"{via}:{connection:D}");

    /// <summary>
    /// The id a caller gets as <see cref="ClientIdClaim"/> unless <see cref="HubPassOptions.ClientIds"/> names another:
    /// the same for the same slug everywhere, so what it writes stays its own across restarts.
    /// </summary>
    /// <param name="slug">The caller's slug.</param>
    /// <returns>A version 8 UUID made of the SHA-256 of <c>hub:&lt;slug&gt;</c>.</returns>
    public static Guid ClientIdOf(string slug)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"hub:{slug}"))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
