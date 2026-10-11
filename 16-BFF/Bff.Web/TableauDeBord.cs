namespace Bff.Web;

/// <summary>Repère de l'assemblage, pour les tests.</summary>
public sealed class Marqueur;

public sealed record VueConseiller(string Nom, string Telephone, string Email, string Agence);
public sealed record VueClient(string Prenom, string Nom, string Email, VueConseiller Conseiller);
public sealed record VueOperation(DateTimeOffset Date, string Libelle, string Categorie, decimal Montant);
public sealed record VueCompte(Guid Id, string Libelle, string Type, string Iban, decimal Solde, string Devise, int NombreOperations, IReadOnlyList<VueOperation> Operations);
public sealed record VueDepense(string Categorie, decimal Total);
public sealed record VueOffre(string Titre, string Texte, string Image);

/// <summary>Exactement ce que l'écran d'accueil du site affiche, en une seule réponse.</summary>
public sealed record VueTableauDeBord(
    VueClient Client, IReadOnlyList<VueCompte> Comptes, IReadOnlyList<VueDepense> DepensesParCategorie,
    IReadOnlyList<VueOffre>? Offres, IReadOnlyList<string> Degrade);

/// <summary>
/// L'agrégation : un appel du navigateur, sept appels aux services, faits côté serveur (réseau rapide, en parallèle).
/// Sans BFF, c'est le navigateur qui enchaînerait ces sept appels, puis assemblerait et calculerait lui-même.
/// </summary>
public sealed class TableauDeBord(ClientServices services, IConfiguration configuration)
{
    private TimeSpan BudgetOffres => TimeSpan.FromMilliseconds(configuration.GetValue("Bff:BudgetOffresMs", 800));

    public async Task<VueTableauDeBord> Construire(string jeton)
    {
        // ① Le profil d'abord : il dit quels comptes afficher, quel conseiller, quel segment d'offres.
        var profil = await services.Profil(jeton);

        // ② Puis tout le reste EN PARALLÈLE : la durée totale est celle de l'appel le plus lent, pas la somme.
        var conseiller = services.Conseiller(profil.ConseillerId, jeton);
        var offres = Facultatif(annulation => services.Offres(profil.Segment, jeton, annulation), BudgetOffres);
        var comptes = profil.Comptes.Select(id => CompteAvecOperations(id, jeton)).ToList();
        Task[] appels = [conseiller, offres, .. comptes];
        await Task.WhenAll(appels);

        // ③ Assembler et calculer pour l'écran : le navigateur n'a plus qu'à afficher.
        var vuesComptes = comptes.Select(c => c.Result).ToList();
        var depenses = vuesComptes.SelectMany(c => c.Operations).Where(o => o.Montant < 0)
            .GroupBy(o => o.Categorie)
            .Select(g => new VueDepense(g.Key, -g.Sum(o => o.Montant)))
            .OrderByDescending(d => d.Total).ToList();
        var c = conseiller.Result;

        return new VueTableauDeBord(
            new VueClient(profil.Prenom, profil.Nom, profil.Email, new VueConseiller(c.Nom, c.Telephone, c.Email, c.Agence)),
            vuesComptes,
            depenses,
            offres.Result?.Select(o => new VueOffre(o.Titre, o.Texte, o.Image)).ToList(),
            offres.Result is null ? ["offres"] : []);
    }

    private async Task<VueCompte> CompteAvecOperations(Guid id, string jeton)
    {
        var compte = services.Compte(id, jeton);
        var operations = services.Operations(id, 10, jeton);
        await Task.WhenAll(compte, operations);
        var (c, o) = (compte.Result, operations.Result);
        return new VueCompte(c.Id, c.Libelle, c.Type, c.Iban, c.Solde, c.Devise, o.Total,
            o.Operations.Select(x => new VueOperation(x.Date, x.LibelleBrut, x.Categorie, x.Montant)).ToList());
    }

    /// <summary>
    /// Les offres sont accessoires : en panne ou trop lentes, l'écran s'affiche sans elles (« dégradé »)
    /// au lieu d'échouer en entier. Le budget de temps évite qu'un service lent ralentisse toute la page.
    /// </summary>
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
