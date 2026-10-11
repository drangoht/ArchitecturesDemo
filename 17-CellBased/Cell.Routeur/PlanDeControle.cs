using System.Net.Http.Json;

namespace Cell.Routeur;

// Format d'échange avec les cellules (recopié : le routeur ne référence pas le code des cellules).
public sealed record CompteExport(Guid Id, string Libelle, decimal Solde);
public sealed record MouvementExport(Guid CompteId, DateTimeOffset Date, string Libelle, decimal Montant);
public sealed record ClientExport(string Id, string Nom, List<CompteExport> Comptes, List<MouvementExport> Mouvements, List<Guid> OperationsTraitees);
public sealed record Sante(string Cellule, string Version, bool Sain, string Detail, int Clients);

public sealed record EtatDeCellule(string Id, string Url, int Capacite, int Clients, bool Ouverte, string Version, bool Joignable, bool? Sain, string? Detail);
public sealed record VagueDeploiement(int Vague, IReadOnlyList<string> Cellules, string Resultat);
public sealed record BilanDeploiement(string Version, string Statut, IReadOnlyList<VagueDeploiement> Vagues);
public sealed record BilanMigration(string Client, string De, string Vers, int Comptes, int Mouvements);

public sealed class PlanException(int statut, string message) : Exception(message)
{
    public int Statut => statut;
}

/// <summary>
/// LE PLAN DE CONTRÔLE : il décide (placer un client, ajouter une cellule, déplacer un client, déployer une version)
/// et écrit la table de placement. Il peut être en panne sans que le trafic s'arrête : le routeur et les cellules
/// continuent avec la table telle qu'elle est. En production, c'est un service à part, hors du chemin des requêtes.
/// </summary>
public sealed class PlanDeControle(RegistreCellules registre, TablePlacement placement, IHttpClientFactory fabrique, IConfiguration configuration)
{
    private TimeSpan Observation => TimeSpan.FromMilliseconds(configuration.GetValue("Deploiement:ObservationMs", 1_000));

    /// <summary>La cellule ouverte la moins chargée qui a encore de la place.</summary>
    public Cellule? ChoisirCellule() => registre.Toutes()
        .Where(c => c.Ouverte && placement.Nombre(c.Id) < c.Capacite)
        .OrderBy(c => placement.Nombre(c.Id)).ThenBy(c => c.Id)
        .FirstOrDefault();

    /// <summary>
    /// Un nouveau client. Quand toutes les cellules sont pleines, on n'agrandit pas une cellule : on en AJOUTE une
    /// (POST /plateforme/cellules). Toutes les cellules gardent une taille connue, testée, et un rayon d'impact borné.
    /// </summary>
    public async Task<Cellule> CreerClient(ClientExport client)
    {
        if (placement.CelluleDe(client.Id) is not null)
            throw new PlanException(StatusCodes.Status409Conflict, $"Le client « {client.Id} » existe déjà.");
        var cellule = ChoisirCellule()
            ?? throw new PlanException(StatusCodes.Status503ServiceUnavailable, "Toutes les cellules sont pleines : provisionner une nouvelle cellule.");

        var reponse = await Client(cellule).PostAsJsonAsync(Url(cellule, "/interne/clients"), client);
        await Verifier(reponse, cellule);
        placement.Placer(client.Id, cellule.Id);
        return cellule;
    }

    /// <summary>Une cellule vient d'être provisionnée (infrastructure créée, application déployée) : on l'ouvre aux nouveaux clients.</summary>
    public async Task<Cellule> Enregistrer(Cellule cellule)
    {
        if (registre.Trouver(cellule.Id) is not null)
            throw new PlanException(StatusCodes.Status409Conflict, $"La cellule « {cellule.Id} » existe déjà.");
        var sante = await LireSante(cellule)
            ?? throw new PlanException(StatusCodes.Status400BadRequest, $"La cellule « {cellule.Id} » ne répond pas sur {cellule.Url}.");
        var enregistree = cellule with { Version = sante.Version };
        registre.Ajouter(enregistree);
        return enregistree;
    }

    /// <summary>Plus de nouveaux clients dans cette cellule (avant de la vider, ou si elle approche d'une limite).</summary>
    public bool Fermer(string id)
    {
        if (registre.Trouver(id) is not { } cellule)
            return false;
        registre.Remplacer(cellule with { Ouverte = false });
        return true;
    }

    /// <summary>
    /// Déplacer un client (rééquilibrage, client devenu trop gros, cellule à vider). Le client est indisponible quelques
    /// instants (503 + Retry-After) ; les autres clients des deux cellules ne voient rien.
    /// </summary>
    public async Task<BilanMigration> Migrer(string clientId, string vers)
    {
        var source = placement.CelluleDe(clientId) is { } id ? registre.Trouver(id)! : throw new PlanException(StatusCodes.Status404NotFound, $"Client « {clientId} » inconnu.");
        var cible = registre.Trouver(vers) ?? throw new PlanException(StatusCodes.Status404NotFound, $"Cellule « {vers} » inconnue.");
        if (source.Id == cible.Id)
            throw new PlanException(StatusCodes.Status400BadRequest, $"Le client est déjà dans {cible.Id}.");
        if (placement.Nombre(cible.Id) >= cible.Capacite)
            throw new PlanException(StatusCodes.Status400BadRequest, $"La cellule {cible.Id} est pleine.");

        placement.DebuterMigration(clientId);   // ① plus aucune écriture pendant la copie
        try
        {
            var export = await Client(source).GetFromJsonAsync<ClientExport>(Url(source, $"/interne/clients/{clientId}/export"));
            await Verifier(await Client(cible).PostAsJsonAsync(Url(cible, "/interne/clients"), export), cible);   // ② copier
            placement.Placer(clientId, cible.Id);                                                                 // ③ basculer le routage
            await Client(source).DeleteAsync(Url(source, $"/interne/clients/{clientId}"));                         // ④ nettoyer
            return new BilanMigration(clientId, source.Id, cible.Id, export!.Comptes.Count, export.Mouvements.Count);
        }
        finally
        {
            placement.TerminerMigration(clientId);
        }
    }

    /// <summary>
    /// Déploiement par vagues : d'abord UNE cellule (le canari), puis les autres. Après chaque vague, on observe la santé ;
    /// au moindre problème, on revient à la version précédente et on s'arrête. Un mauvais déploiement ne touche alors
    /// que les clients d'une cellule, pendant quelques instants.
    /// </summary>
    public async Task<BilanDeploiement> Deployer(string version)
    {
        var cellules = registre.Toutes();
        List<Cellule[]> vagues = [[.. cellules.Take(1)], [.. cellules.Skip(1)]];
        var rapport = new List<VagueDeploiement>();

        for (var n = 0; n < vagues.Count; n++)
        {
            var vague = vagues[n];
            var precedentes = vague.ToDictionary(c => c.Id, c => registre.Trouver(c.Id)!.Version);
            var problemes = new List<string>();
            foreach (var cellule in vague)
            {
                if (await ChangerVersion(cellule, version) is { } refus)
                    problemes.Add($"{cellule.Id} : {refus}");
                else
                    registre.Remplacer(registre.Trouver(cellule.Id)! with { Version = version });
            }

            await Task.Delay(Observation);   // laisser le trafic réel et les sondes s'exprimer
            foreach (var cellule in vague)
            {
                var sante = await LireSante(cellule);
                if (sante is not { Sain: true })
                    problemes.Add($"{cellule.Id} : {sante?.Detail ?? "injoignable"}");
            }

            if (problemes.Count > 0)
            {
                foreach (var cellule in vague)
                {
                    await ChangerVersion(cellule, precedentes[cellule.Id]);   // une cellule injoignable reprendra sa version à son retour
                    registre.Remplacer(registre.Trouver(cellule.Id)! with { Version = precedentes[cellule.Id] });
                }
                rapport.Add(new VagueDeploiement(n + 1, vague.Select(c => c.Id).ToList(), "retour arrière : " + string.Join(" ; ", problemes.Distinct())));
                return new BilanDeploiement(version, "Annule", rapport);
            }
            rapport.Add(new VagueDeploiement(n + 1, vague.Select(c => c.Id).ToList(), "saine"));
        }
        return new BilanDeploiement(version, "Termine", rapport);
    }

    public async Task<IReadOnlyList<EtatDeCellule>> Etat()
    {
        var etats = await Task.WhenAll(registre.Toutes().Select(async c =>
        {
            var sante = await LireSante(c);
            return new EtatDeCellule(c.Id, c.Url, c.Capacite, placement.Nombre(c.Id), c.Ouverte, sante?.Version ?? c.Version,
                sante is not null, sante?.Sain, sante?.Detail);
        }));
        return etats;
    }

    /// <summary>null si la cellule a pris la version, sinon la raison du refus.</summary>
    private async Task<string?> ChangerVersion(Cellule cellule, string version)
    {
        try
        {
            var reponse = await Client(cellule).PutAsJsonAsync(Url(cellule, "/admin/version"), new { version });
            return reponse.IsSuccessStatusCode ? null : $"refusé ({(int)reponse.StatusCode})";
        }
        catch (HttpRequestException)
        {
            return "injoignable";
        }
    }

    private async Task<Sante?> LireSante(Cellule cellule)
    {
        try
        {
            var reponse = await Client(cellule).GetAsync(Url(cellule, "/sante"));
            return reponse.IsSuccessStatusCode ? await reponse.Content.ReadFromJsonAsync<Sante>() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private HttpClient Client(Cellule cellule) => fabrique.CreateClient($"cellule-{cellule.Id}");

    private static Uri Url(Cellule cellule, string chemin) => new(new Uri(cellule.Url), chemin);

    private static async Task Verifier(HttpResponseMessage reponse, Cellule cellule)
    {
        if (!reponse.IsSuccessStatusCode)
            throw new PlanException(StatusCodes.Status502BadGateway,
                $"La cellule {cellule.Id} a refusé ({(int)reponse.StatusCode}) : {await reponse.Content.ReadAsStringAsync()}");
    }
}
