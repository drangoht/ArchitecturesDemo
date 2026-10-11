using System.Collections.Immutable;
using Espace.Grille;

namespace Espace.Banque;

/// <summary>Un compte, tel qu'il vit dans la grille : immuable, chaque écriture le remplace par une nouvelle version.</summary>
public sealed record Compte(Guid Id, string Titulaire, decimal Solde, ImmutableList<OperationDto> Operations);

public sealed record OperationDto(DateTimeOffset Date, string Libelle, decimal Montant);

// ── Requêtes : la clé de routage est le compte concerné ──
public sealed record OuvrirCompte(Guid Id, string Titulaire, decimal DepotInitial) : IRequete
{
    public string Cle => Id.ToString();
}

public sealed record ConsulterCompte(Guid Id) : IRequete
{
    public string Cle => Id.ToString();
}

public sealed record Deposer(Guid Id, decimal Montant) : IRequete
{
    public string Cle => Id.ToString();
}

public sealed record Retirer(Guid Id, decimal Montant) : IRequete
{
    public string Cle => Id.ToString();
}

/// <summary>Traité par l'unité du compte source.</summary>
public sealed record Virer(Guid Source, Guid Cible, decimal Montant) : IRequete
{
    public string Cle => Source.ToString();
}

/// <summary>Le crédit d'un virement, envoyé par l'unité de la source à celle de la cible.</summary>
public sealed record Crediter(Guid Id, decimal Montant, string Libelle) : IRequeteInterne
{
    public string Cle => Id.ToString();
}

// ── Réponses ──
public sealed record CompteDto(Guid Id, string Titulaire, decimal Solde);
public sealed record VirementDto(Guid Source, Guid Cible, decimal Montant, decimal SoldeSource);
public sealed record Refus(string Motif, bool Introuvable = false);

/// <summary>
/// Le métier d'une unité de traitement. Il ne voit QUE la réplique de son unité : lire un compte est un accès mémoire,
/// écrire aussi ; la diffusion aux autres unités et l'écriture dans la base se font après, sans lui.
/// </summary>
public static class Traitement
{
    public static async Task<object> Traiter(object requete, ContexteUnite unite) => requete switch
    {
        OuvrirCompte r => unite.Modifier<Compte>(r.Cle, c =>
            c is not null ? (null, new Refus("Ce compte existe déjà."))
            : string.IsNullOrWhiteSpace(r.Titulaire) || r.DepotInitial < 0 ? (null, new Refus("Titulaire requis et dépôt initial positif ou nul."))
            : Nouveau(new Compte(r.Id, r.Titulaire.Trim(), r.DepotInitial, [new OperationDto(DateTimeOffset.UtcNow, "Dépôt initial", r.DepotInitial)]))),
        ConsulterCompte r => unite.Lire<Compte>(r.Cle) is { } compte ? Vue(compte) : Introuvable(r.Id),
        Deposer r => Mouvement(unite, r.Id, r.Montant, debit: false, "Dépôt"),
        Crediter r => Mouvement(unite, r.Id, r.Montant, debit: false, r.Libelle),
        Retirer r => Mouvement(unite, r.Id, r.Montant, debit: true, "Retrait"),
        Virer r => await Virer(unite, r),
        _ => throw new InvalidOperationException($"Requête inconnue : {requete.GetType().Name}.")
    };

    /// <summary>Lire, vérifier, écrire : atomique dans la réplique de CETTE unité seulement.</summary>
    private static object Mouvement(ContexteUnite unite, Guid id, decimal montant, bool debit, string libelle) =>
        unite.Modifier<Compte>(id.ToString(), compte =>
            compte is null ? (null, Introuvable(id))
            : montant <= 0 ? (null, new Refus("Le montant doit être positif."))
            : debit && montant > compte.Solde ? (null, new Refus($"Solde insuffisant ({compte.Solde:0.00} €)."))
            : Nouveau(compte with
            {
                Solde = compte.Solde + (debit ? -montant : montant),
                Operations = compte.Operations.Add(new OperationDto(DateTimeOffset.UtcNow, libelle, debit ? -montant : montant))
            }));

    /// <summary>
    /// Débit ici, crédit par l'unité de la cible (envoyé par la grille de messages), compensation si le crédit échoue.
    /// Pas de transaction : les deux comptes peuvent appartenir à deux unités différentes.
    /// </summary>
    private static async Task<object> Virer(ContexteUnite unite, Virer r)
    {
        if (r.Source == r.Cible)
            return new Refus("Les comptes source et cible doivent être différents.");
        // Toutes les données sont dans chaque réplique : vérifier la cible ne coûte qu'un accès mémoire.
        if (unite.Lire<Compte>(r.Cible.ToString()) is null)
            return Introuvable(r.Cible);

        var debit = Mouvement(unite, r.Source, r.Montant, debit: true, "Virement émis");
        if (debit is not CompteDto source)
            return debit;

        if (await unite.Envoyer(new Crediter(r.Cible, r.Montant, "Virement reçu")) is Refus refusCible)
        {
            Mouvement(unite, r.Source, r.Montant, debit: false, "Annulation du virement");
            return refusCible;
        }
        return new VirementDto(r.Source, r.Cible, r.Montant, source.Solde);
    }

    private static (Compte, object) Nouveau(Compte compte) => (compte, Vue(compte));

    private static CompteDto Vue(Compte compte) => new(compte.Id, compte.Titulaire, compte.Solde);

    private static Refus Introuvable(Guid id) => new($"Compte {id} introuvable.", Introuvable: true);
}

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Le contenu initial de la BASE : la grille le chargera au premier démarrage, par le lecteur de données.</summary>
    public static IEnumerable<Entree> Comptes() =>
    [
        Entree(new Compte(Alice, "Alice", 1_000m, [new OperationDto(DateTimeOffset.UtcNow, "Dépôt initial", 1_000m)])),
        Entree(new Compte(Bob, "Bob", 500m, [new OperationDto(DateTimeOffset.UtcNow, "Dépôt initial", 500m)]))
    ];

    private static Entree Entree(Compte compte) => new(compte.Id.ToString(), compte, new Grille.Version(1, "base"));
}
