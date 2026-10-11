using System.Globalization;
using System.Text;

namespace Bff.Mobile;

// Des réponses courtes : un petit écran, un réseau mobile lent et facturé au volume.
public sealed record CompteMobile(Guid Id, string Nom, decimal Solde);
public sealed record OperationMobile(DateOnly Date, string Libelle, decimal Montant);
public sealed record OffreMobile(string Titre);
public sealed record AccueilMobile(string Prenom, decimal SoldeTotal, IReadOnlyList<CompteMobile> Comptes,
    IReadOnlyList<OperationMobile> DernieresOperations, OffreMobile? Offre = null);
public sealed record PageMobile(IReadOnlyList<OperationMobile> Operations, string? Suivant);

/// <summary>Les écrans de l'application mobile : ce que chaque écran affiche, ni plus ni moins.</summary>
public sealed class Ecrans(ClientServices services, IConfiguration configuration)
{
    private const int TaillePage = 10;

    private TimeSpan BudgetOffre => TimeSpan.FromMilliseconds(configuration.GetValue("Bff:BudgetOffreMs", 300));

    /// <summary>
    /// L'écran d'accueil. v1 : soldes et 3 dernières opérations. v2 (nouvelle version de l'application) : en plus,
    /// un bandeau d'offre. Les deux versions coexistent, car les téléphones ne se mettent pas tous à jour : c'est le BFF
    /// qui absorbe cette évolution, les services n'ont pas changé.
    /// </summary>
    public async Task<AccueilMobile> Accueil(string jeton, int version)
    {
        var profil = await services.Profil(jeton);

        var comptes = profil.Comptes.Select(id => services.Compte(id, jeton)).ToList();
        var operations = profil.Comptes.Select(id => services.Operations(id, 1, 3, jeton)).ToList();
        var offre = version >= 2 ? Facultatif(a => services.Offres(profil.Segment, jeton, a), BudgetOffre) : Task.FromResult<OffreAval[]?>(null);
        Task[] appels = [.. comptes, .. operations, offre];
        await Task.WhenAll(appels);

        var vuesComptes = comptes.Select(c => new CompteMobile(c.Result.Id, c.Result.Libelle, c.Result.Solde)).ToList();
        var dernieres = operations.SelectMany(o => o.Result.Operations)
            .OrderByDescending(o => o.Date).Take(3).Select(Vue).ToList();
        var premiereOffre = offre.Result?.FirstOrDefault();

        return new AccueilMobile(profil.Prenom, vuesComptes.Sum(c => c.Solde), vuesComptes, dernieres,
            premiereOffre is null ? null : new OffreMobile(premiereOffre.Titre));
    }

    /// <summary>
    /// Défilement infini : l'application demande « la suite » avec un curseur opaque, sans connaître la pagination
    /// des services (page/taille), que le BFF pourra changer sans mettre à jour l'application.
    /// </summary>
    public async Task<PageMobile> Operations(Guid compte, string? curseur, string jeton)
    {
        var page = LirePage(curseur);
        var resultat = await services.Operations(compte, page, TaillePage, jeton);
        var suivant = page * TaillePage < resultat.Total ? Curseur(page + 1) : null;
        return new PageMobile(resultat.Operations.Select(Vue).ToList(), suivant);
    }

    private static OperationMobile Vue(OperationAval o) => new(DateOnly.FromDateTime(o.Date.UtcDateTime), LibelleCourt(o.LibelleBrut), o.Montant);

    // Hexadécimal : opaque pour l'application, et sans caractère à échapper dans une URL.
    private static string Curseur(int page) => Convert.ToHexString(Encoding.UTF8.GetBytes($"page:{page}"));

    private static int LirePage(string? curseur)
    {
        if (string.IsNullOrEmpty(curseur))
            return 1;
        try
        {
            var texte = Encoding.UTF8.GetString(Convert.FromHexString(curseur));
            return texte.StartsWith("page:") && int.TryParse(texte[5..], out var page) && page > 0 ? page : 1;
        }
        catch (FormatException)
        {
            return 1;
        }
    }

    private static readonly (string Prefixe, string? Remplacement)[] Prefixes =
    [
        ("RETRAIT DAB", "Retrait d'espèces"), ("VIR SEPA EMIS", "Virement émis"), ("VIR SEPA RECU", "Virement reçu"),
        ("VIR SEPA DEPUIS", "Virement reçu"), ("VIR SEPA SALAIRE", "Salaire"), ("PRLV SEPA", null), ("CB", null)
    ];

    private static readonly HashSet<string> Sigles = ["EDF", "SNCF", "EU"];
    private static readonly HashSet<string> MotsInutiles = ["SARL", "SAS"];

    /// <summary>« CB CARREFOUR 0910 PARIS 15 » → « Carrefour » : sur un téléphone, le libellé bancaire brut ne tient pas.</summary>
    public static string LibelleCourt(string brut)
    {
        foreach (var (prefixe, remplacement) in Prefixes)
        {
            if (!brut.StartsWith(prefixe + " "))
                continue;
            if (remplacement is not null)
                return remplacement;
            var mots = brut[prefixe.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .TakeWhile(m => !m.Any(char.IsDigit))
                .Where(m => !MotsInutiles.Contains(m))
                .Select(m => Sigles.Contains(m) ? m : CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(m.ToLowerInvariant()));
            return string.Join(' ', mots);
        }
        return brut;
    }

    private static async Task<T?> Facultatif<T>(Func<CancellationToken, Task<T>> appel, TimeSpan budget) where T : class
    {
        using var delai = new CancellationTokenSource(budget);
        try
        {
            return await appel(delai.Token);
        }
        catch (Exception e) when (e is ServiceAvalException or HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }
}
