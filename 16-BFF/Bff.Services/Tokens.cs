using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bff.Services;

/// <summary>
/// An identity provider reduced to the minimum: a signed access token (HMAC), valid for 15 minutes.
/// In reality: an OpenID Connect server (Entra ID, Keycloak, Auth0…) and JWTs. The format matters little here;
/// what counts for the demo is WHERE this token is kept (browser or server).
/// </summary>
public sealed class Tokens(TimeProvider clock)
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    private sealed record Payload(string Sub, long Exp);

    public string Issue(string user)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(user, (clock.GetUtcNow() + Duration).ToUnixTimeSeconds()));
        return $"{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(_key, payload))}";
    }

    /// <summary>The user of the token, or null if the token is forged or expired.</summary>
    public string? Validate(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2)
                return null;
            var payload = FromBase64Url(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, payload), FromBase64Url(parts[1])))
                return null;
            var c = JsonSerializer.Deserialize<Payload>(payload)!;
            return c.Exp > clock.GetUtcNow().ToUnixTimeSeconds() ? c.Sub : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var b64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
    }
}
