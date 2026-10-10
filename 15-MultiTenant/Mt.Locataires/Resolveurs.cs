using System.Net;
using Microsoft.AspNetCore.Http;

namespace Mt.Locataires;

/// <summary>Une façon de reconnaître le locataire d'une requête. Plusieurs peuvent coexister.</summary>
public interface IResolveurLocataire
{
    string Nom { get; }
    string? Resoudre(HttpContext http);
}

/// <summary><c>alpha.localhost:5114</c> → « alpha ». C'est le choix habituel des SaaS : chaque client a son adresse.</summary>
public sealed class ResolveurParSousDomaine(string domaineRacine = "localhost") : IResolveurLocataire
{
    public string Nom => "sous-domaine";

    public string? Resoudre(HttpContext http)
    {
        var hote = http.Request.Host.Host;
        if (IPAddress.TryParse(hote, out _) || !hote.EndsWith("." + domaineRacine, StringComparison.OrdinalIgnoreCase))
            return null;
        var sousDomaine = hote[..^(domaineRacine.Length + 1)];
        return sousDomaine.Contains('.') ? null : sousDomaine;
    }
}

/// <summary>
/// En-tête <c>X-Locataire</c> : pratique entre services ou pour une API. Seul, il est déclaratif : en production,
/// on vérifie que l'utilisateur authentifié appartient bien à ce locataire (ou on lit le locataire dans son jeton).
/// </summary>
public sealed class ResolveurParEnTete : IResolveurLocataire
{
    public const string EnTete = "X-Locataire";

    public string Nom => "en-tête";

    public string? Resoudre(HttpContext http) =>
        http.Request.Headers.TryGetValue(EnTete, out var valeur) && !string.IsNullOrWhiteSpace(valeur) ? valeur.ToString().Trim() : null;
}
