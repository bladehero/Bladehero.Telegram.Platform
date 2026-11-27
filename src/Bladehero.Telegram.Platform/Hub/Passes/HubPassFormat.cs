using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Hub;

// A pass as the hub writes it: a compact JWT, ES256 and nothing else. Shared by the verifier and the fake hub.
internal static class HubPassFormat
{
    public const string Algorithm = "ES256";
    public const string Type = "hub-pass+jwt";
    public const string KeysPath = "/hub/keys";

    // The named HttpClient the verifier reads the hub's keys with, so a test can point it at a fake.
    public const string KeysClient = "Bladehero.Telegram.Platform.HubPassKeys";

    private static readonly JsonSerializerOptions Plain = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Three parts and no spaces: what tells a pass from an app's own key on the same header.
    public static bool LooksLikeAPass(string token) =>
        token.Length > 0 && token.Count('.') == 2 && !token.Any(char.IsWhiteSpace);

    public static string Sign(JsonObject header, JsonObject claims, ECDsa key)
    {
        var signed = $"{Part(header)}.{Part(claims)}";
        return $"{signed}.{Base64Url.EncodeToString(key.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256))}";
    }

    public static bool TryRead(string token, [NotNullWhen(true)] out ReadPass? pass)
    {
        pass = null;
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            if (
                JsonNode.Parse(Base64Url.DecodeFromChars(parts[0])) is not JsonObject header
                || JsonNode.Parse(Base64Url.DecodeFromChars(parts[1])) is not JsonObject claims
            )
            {
                return false;
            }

            pass = new ReadPass(
                header,
                claims,
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
                Base64Url.DecodeFromChars(parts[2])
            );
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    public static ECParameters PublicKey(byte[] x, byte[] y) =>
        new()
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y },
        };

    public static JsonObject Jwk(string id, ECDsa key)
    {
        var point = key.ExportParameters(includePrivateParameters: false).Q;
        return new JsonObject
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["kid"] = id,
            ["use"] = "sig",
            ["alg"] = Algorithm,
            ["x"] = Base64Url.EncodeToString(point.X),
            ["y"] = Base64Url.EncodeToString(point.Y),
        };
    }

    private static string Part(JsonObject json) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json.ToJsonString(Plain)));
}

internal sealed record ReadPass(JsonObject Header, JsonObject Claims, byte[] Signed, byte[] Signature);
