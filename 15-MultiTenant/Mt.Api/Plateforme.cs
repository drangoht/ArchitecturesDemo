using Microsoft.EntityFrameworkCore;
using Mt.Banque;
using Mt.Locataires;

namespace Mt.Api;

public sealed record BilanDeplacement(string Locataire, string De, string Vers, int Comptes, int Operations);

public sealed record BilanFrais(string Locataire, decimal FraisMensuels, int ComptesPreleves, string? Remarque = null);

public sealed record LignesParLocataire(string Locataire, int Comptes, int Operations);

public sealed record InventaireBase(string Base, IReadOnlyList<LignesParLocataire> Locataires);

/// <summary>
/// Le back-office de l'éditeur : il travaille SUR les locataires, pas POUR l'un d'eux. C'est le seul code qui manipule
/// plusieurs locataires, et il le fait toujours explicitement (locataire désigné, ou IgnoreQueryFilters).
/// En production, ces opérations sont réservées aux administrateurs de la plateforme.
/// </summary>
public sealed class ServicePlateforme(CatalogueLocataires catalogue, FabriqueBanque fabrique, IServiceScopeFactory scopes, TimeProvider horloge)
{
    /// <summary>Arrivée d'un client : provisionner ses données, puis l'ouvrir. Quelques secondes, aucun déploiement.</summary>
    public async Task<Locataire> Inscrire(DefinitionLocataire definition, IEnumerable<Compte>? comptesInitiaux = null)
    {
        if (!Locataire.IdentifiantValide(definition.Id))
            throw new RegleMetierException("Identifiant invalide : 2 à 30 caractères, minuscules, chiffres et tirets.");
        if (catalogue.Trouver(definition.Id) is not null)
            throw new RegleMetierException($"Le locataire « {definition.Id} » existe déjà.");

        var locataire = Formules.Creer(definition);
        await using (var db = fabrique.Ouvrir(locataire))
        {
            // Premium : crée sa base dédiée. Standard : la base partagée existe déjà, rien à créer.
            await db.Database.EnsureCreatedAsync();
            foreach (var compte in comptesInitiaux ?? [])
            {
                db.Comptes.Add(compte);
                db.Operations.Add(new Operation(compte.Id, horloge.GetUtcNow(), "Dépôt initial", compte.Solde));
            }
            await db.SaveChangesAsync();
        }
        catalogue.Ajouter(locataire);
        return locataire;
    }

    public bool Suspendre(string id, bool suspendu)
    {
        if (catalogue.Trouver(id) is not { } locataire)
            return false;
        catalogue.Remplacer(locataire with { Actif = !suspendu });
        return true;
    }

    /// <summary>
    /// Départ d'un client : effacer TOUTES ses données (RGPD, fin de contrat).
    /// Base dédiée : on supprime la base. Base partagée : on supprime ses lignes, une table après l'autre.
    /// </summary>
    public async Task<BilanDeplacement?> Supprimer(string id)
    {
        if (catalogue.Trouver(id) is not { } locataire)
            return null;
        // Couper l'accès d'abord : plus aucune requête n'arrive pendant l'effacement.
        catalogue.Remplacer(locataire with { Actif = false });

        var bilan = await Compter(locataire, "(supprimé)");
        if (locataire.Isolation == Isolation.BaseDediee)
            await fabrique.SupprimerBase(locataire);
        else
            await ViderBasePartagee(locataire);

        catalogue.Retirer(id);
        return bilan;
    }

    /// <summary>
    /// Changement de formule. Si l'isolation change (Standard ↔ Premium), les données déménagent d'une base à l'autre ;
    /// le locataire est suspendu pendant le déménagement, pour qu'aucune écriture ne se perde entre les deux bases.
    /// </summary>
    public async Task<BilanDeplacement?> ChangerDeFormule(string id, Plan plan)
    {
        if (catalogue.Trouver(id) is not { } avant)
            return null;
        var apres = Formules.Changer(avant, plan);
        if (avant.Isolation == apres.Isolation)
        {
            catalogue.Remplacer(apres);
            return new BilanDeplacement(id, FabriqueBanque.NomBase(avant), FabriqueBanque.NomBase(apres), 0, 0);
        }

        catalogue.Remplacer(avant with { Actif = false });
        try
        {
            BilanDeplacement bilan;
            await using (var source = fabrique.Ouvrir(avant))
            await using (var destination = fabrique.Ouvrir(apres))
            {
                // Le filtre global fait le tri : la source ne renvoie que les lignes de CE locataire.
                var comptes = await source.Comptes.AsNoTracking().ToListAsync();
                var operations = await source.Operations.AsNoTracking().ToListAsync();
                await destination.Database.EnsureCreatedAsync();
                destination.Comptes.AddRange(comptes);
                destination.Operations.AddRange(operations);
                await destination.SaveChangesAsync();
                bilan = new BilanDeplacement(id, FabriqueBanque.NomBase(avant), FabriqueBanque.NomBase(apres), comptes.Count, operations.Count);
            }

            // On n'efface l'ancien emplacement qu'une fois la copie enregistrée.
            if (avant.Isolation == Isolation.BaseDediee)
                await fabrique.SupprimerBase(avant);
            else
                await ViderBasePartagee(avant);

            catalogue.Remplacer(apres with { Actif = avant.Actif });
            return bilan;
        }
        catch
        {
            catalogue.Remplacer(avant);
            throw;
        }
    }

    /// <summary>Ce que contient chaque base, locataire par locataire : on voit les deux modèles d'isolation côte à côte.</summary>
    public async Task<IReadOnlyList<InventaireBase>> Inventaire()
    {
        var inventaire = new List<InventaireBase>();
        foreach (var nom in fabrique.Bases)
        {
            await using var db = fabrique.OuvrirSansLocataire(nom);
            // Sans IgnoreQueryFilters(), ces requêtes lèveraient AucunLocataireException.
            var comptes = await db.Comptes.IgnoreQueryFilters().Select(c => c.LocataireId).ToListAsync();
            var operations = await db.Operations.IgnoreQueryFilters().Select(o => o.LocataireId).ToListAsync();
            var lignes = comptes.Concat(operations).Distinct().Order()
                .Select(l => new LignesParLocataire(l, comptes.Count(c => c == l), operations.Count(o => o == l)))
                .ToList();
            inventaire.Add(new InventaireBase(nom, lignes));
        }
        return inventaire;
    }

    /// <summary>
    /// Une tâche de fond n'a pas de requête HTTP, donc pas de middleware pour fixer le locataire : c'est à elle de le faire,
    /// locataire par locataire, chacun dans son propre scope (son DbContext, ses règles). Si elle l'oubliait, le DbContext
    /// lèverait AucunLocataireException au lieu de travailler sur toutes les banques à la fois.
    /// </summary>
    public async Task<IReadOnlyList<BilanFrais>> PreleverFraisMensuels(string mois)
    {
        var bilans = new List<BilanFrais>();
        foreach (var locataire in catalogue.Tous())
        {
            if (!locataire.Actif)
            {
                bilans.Add(new BilanFrais(locataire.Id, locataire.Regles.FraisMensuels, 0, "suspendu : ignoré"));
                continue;
            }
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ContexteLocataire>().Definir(locataire);
            var preleves = await scope.ServiceProvider.GetRequiredService<ServiceBancaire>().PreleverFraisMensuels(mois);
            bilans.Add(new BilanFrais(locataire.Id, locataire.Regles.FraisMensuels, preleves));
        }
        return bilans;
    }

    private async Task<BilanDeplacement> Compter(Locataire locataire, string vers)
    {
        await using var db = fabrique.Ouvrir(locataire);
        return new BilanDeplacement(locataire.Id, FabriqueBanque.NomBase(locataire), vers,
            await db.Comptes.CountAsync(), await db.Operations.CountAsync());
    }

    private async Task ViderBasePartagee(Locataire locataire)
    {
        await using var db = fabrique.Ouvrir(locataire);
        db.Operations.RemoveRange(await db.Operations.ToListAsync());
        db.Comptes.RemoveRange(await db.Comptes.ToListAsync());
        await db.SaveChangesAsync();
    }
}
