using Acteurs.Noyau;

namespace Acteurs.Banque;

/// <summary>
/// UN acteur par compte bancaire. Son solde est un simple champ, modifié sans verrou ni transaction :
/// le runtime garantit qu'un seul message à la fois s'exécute ici. Mille retraits simultanés sur le même compte
/// sont donc traités l'un après l'autre, et le solde ne peut jamais devenir négatif.
/// </summary>
public sealed class CompteActeur(MagasinEtats magasin, Pieges pieges, TimeProvider horloge) : Acteur
{
    public const string Type = "compte";

    /// <summary>L'état durable d'un compte, tel qu'il est stocké.</summary>
    public sealed class Etat
    {
        public string Titulaire { get; set; } = "";
        public decimal Solde { get; set; }
        public List<Ligne> Operations { get; set; } = [];
    }

    public sealed record Ligne(DateTimeOffset Date, string Libelle, decimal Montant, string? Cle);

    /// <summary>null tant que le compte n'a pas été ouvert : un acteur virtuel « existe » avant son compte.</summary>
    private Etat? _etat;

    private Guid Id => Guid.Parse(Contexte.Cle);

    public override async Task Demarrer() => _etat = await magasin.Lire<Etat>(Contexte.Chemin);

    public override async Task<object?> Recevoir(object message)
    {
        await pieges.Verifier(Contexte.Chemin, Etape.Reception);

        return message switch
        {
            OuvrirCompte m => await Ouvrir(m),
            _ when _etat is null => new Refus($"Compte {Contexte.Cle} introuvable.", Introuvable: true),
            ConsulterCompte => Vue(),
            ListerOperations => _etat.Operations.Select(o => new OperationDto(o.Date, o.Libelle, o.Montant)).ToList(),
            Deposer m => await Mouvement(m.Montant, debit: false, "Dépôt", cle: null),
            Retirer m => await Mouvement(m.Montant, debit: true, "Retrait", cle: null),
            Debiter m => await Mouvement(m.Montant, debit: true, m.Libelle, m.Cle),
            Crediter m => await Mouvement(m.Montant, debit: false, m.Libelle, m.Cle),
            // Un message que l'acteur ne comprend pas est un bogue de l'expéditeur : on plante, le superviseur décide.
            _ => throw new InvalidOperationException($"Message inattendu : {message.GetType().Name}.")
        };
    }

    private async Task<object> Ouvrir(OuvrirCompte m)
    {
        if (_etat is not null)
            return new Refus($"Le compte {Contexte.Cle} est déjà ouvert.");
        if (string.IsNullOrWhiteSpace(m.Titulaire) || m.DepotInitial < 0)
            return new Refus("Titulaire requis et dépôt initial positif ou nul.");

        var etat = new Etat { Titulaire = m.Titulaire.Trim(), Solde = m.DepotInitial };
        etat.Operations.Add(new Ligne(horloge.GetUtcNow(), "Dépôt initial", m.DepotInitial, null));
        await magasin.Ecrire(Contexte.Chemin, etat);
        _etat = etat;
        return Vue();
    }

    /// <summary>Un crédit ou un débit. Lire, vérifier, modifier : sans risque, personne d'autre n'est là.</summary>
    private async Task<object> Mouvement(decimal montant, bool debit, string libelle, string? cle)
    {
        var etat = _etat!;
        // Un virement rejoué (après une panne, un délai dépassé…) renvoie la même clé : déjà appliqué, rien à faire.
        if (cle is not null && etat.Operations.Any(o => o.Cle == cle))
            return Vue();
        if (montant <= 0)
            return new Refus("Le montant doit être positif.");
        if (debit && montant > etat.Solde)
            return new Refus($"Solde insuffisant ({etat.Solde:0.00} €).");

        var signe = debit ? -montant : montant;
        etat.Solde += signe;
        etat.Operations.Add(new Ligne(horloge.GetUtcNow(), libelle, signe, cle));

        // Une panne ici laisse l'état EN MÉMOIRE modifié mais pas le stockage. Aucune importance : le superviseur jette
        // cette incarnation, la suivante recharge le dernier état écrit. Le retrait n'a tout simplement pas eu lieu.
        await pieges.Verifier(Contexte.Chemin, Etape.AvantSauvegarde);
        await magasin.Ecrire(Contexte.Chemin, etat);
        return Vue();
    }

    private CompteDto Vue() => new(Id, _etat!.Titulaire, _etat.Solde);
}
