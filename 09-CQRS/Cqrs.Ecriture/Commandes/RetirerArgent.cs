using Cqrs.Ecriture.Domaine;
using Cqrs.Ecriture.Persistance;

namespace Cqrs.Ecriture.Commandes;

public record RetirerArgent(Guid CompteId, decimal Montant) : ICommande;

public class RetirerArgentValidateur : IValidateur<RetirerArgent>
{
    public IEnumerable<string> Valider(RetirerArgent commande)
    {
        if (commande.Montant <= 0)
            yield return "Le montant doit être strictement positif.";
    }
}

public class RetirerArgentGestionnaire(EcritureDb db, TimeProvider horloge) : IGestionnaireCommande<RetirerArgent>
{
    public async Task Traiter(RetirerArgent commande)
    {
        var compte = await db.Comptes.FindAsync(commande.CompteId) ?? throw new CompteIntrouvableException(commande.CompteId);
        compte.Retirer(commande.Montant, horloge.GetUtcNow().UtcDateTime);
        await db.Enregistrer();
    }
}
