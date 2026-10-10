using Cqrs.Ecriture.Domaine;
using Cqrs.Ecriture.Persistance;
using Cqrs.Evenements;

namespace Cqrs.Ecriture.Commandes;

public record EffectuerVirement(Guid VirementId, Guid SourceId, Guid CibleId, decimal Montant) : ICommande;

public class EffectuerVirementValidateur : IValidateur<EffectuerVirement>
{
    public const decimal Plafond = 5_000m;

    public IEnumerable<string> Valider(EffectuerVirement commande)
    {
        if (commande.Montant <= 0)
            yield return "Le montant doit être strictement positif.";
        if (commande.Montant > Plafond)
            yield return $"Un virement ne peut pas dépasser {Plafond:0} €.";
        if (commande.SourceId == commande.CibleId)
            yield return "Impossible de virer vers le même compte.";
    }
}

public class EffectuerVirementGestionnaire(EcritureDb db, TimeProvider horloge) : IGestionnaireCommande<EffectuerVirement>
{
    public async Task Traiter(EffectuerVirement commande)
    {
        var source = await db.Comptes.FindAsync(commande.SourceId) ?? throw new CompteIntrouvableException(commande.SourceId);
        var cible = await db.Comptes.FindAsync(commande.CibleId) ?? throw new CompteIntrouvableException(commande.CibleId);

        source.Debiter(commande.Montant);
        cible.Crediter(commande.Montant);

        // Les deux comptes et l'événement sont enregistrés ensemble.
        await db.Enregistrer(new VirementEffectue(commande.VirementId, source.Id, cible.Id, commande.Montant,
            source.Solde, cible.Solde, horloge.GetUtcNow().UtcDateTime));
    }
}
