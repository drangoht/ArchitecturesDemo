using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bff.Services;

/// <summary>
/// Un fournisseur d'identité réduit au minimum : un jeton d'accès signé (HMAC), valable 15 minutes.
/// En réalité : un serveur OpenID Connect (Entra ID, Keycloak, Auth0…) et des JWT. Le format importe peu ici ;
/// ce qui compte pour la démo, c'est OÙ ce jeton est conservé (navigateur ou serveur).
/// </summary>
public sealed class Jetons(TimeProvider horloge)
{
    public static readonly TimeSpan Duree = TimeSpan.FromMinutes(15);

    private readonly byte[] _cle = RandomNumberGenerator.GetBytes(32);

    private sealed record Contenu(string Sub, long Exp);

    public string Emettre(string utilisateur)
    {
        var contenu = JsonSerializer.SerializeToUtf8Bytes(new Contenu(utilisateur, (horloge.GetUtcNow() + Duree).ToUnixTimeSeconds()));
        return $"{Base64Url(contenu)}.{Base64Url(HMACSHA256.HashData(_cle, contenu))}";
    }

    /// <summary>L'utilisateur du jeton, ou null si le jeton est falsifié ou expiré.</summary>
    public string? Valider(string jeton)
    {
        try
        {
            var parties = jeton.Split('.');
            if (parties.Length != 2)
                return null;
            var contenu = DepuisBase64Url(parties[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_cle, contenu), DepuisBase64Url(parties[1])))
                return null;
            var c = JsonSerializer.Deserialize<Contenu>(contenu)!;
            return c.Exp > horloge.GetUtcNow().ToUnixTimeSeconds() ? c.Sub : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] octets) => Convert.ToBase64String(octets).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DepuisBase64Url(string texte)
    {
        var b64 = texte.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
    }
}
