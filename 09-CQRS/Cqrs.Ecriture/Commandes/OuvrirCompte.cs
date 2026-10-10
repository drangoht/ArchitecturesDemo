using Cqrs.Ecriture.Domaine;
using Cqrs.Ecriture.Persistance;

namespace Cqrs.Ecriture.Commandes;

/// <summary>L'identifiant est fourni par l'appelant : la commande n'a ainsi rien à renvoyer.</summary>
public record OuvrirCompte(Guid CompteId, string Titulaire, decimal DepotInitial) : ICommande;

public class OuvrirCompteValidateur : IValidateur<OuvrirCompte>
{
    public IEnumerable<string> Valider(OuvrirCompte commande)
    {
        if (string.IsNullOrWhiteSpace(commande.Titulaire))
            yield return "Le titulaire est obligatoire.";
        if (commande.DepotInitial < 10)
            yield return "Le dépôt initial doit être d'au moins 10 €.";
    }
}

public class OuvrirCompteGestionnaire(EcritureDb db, TimeProvider horloge) : IGestionnaireCommande<OuvrirCompte>
{
    public async Task Traiter(OuvrirCompte commande)
    {
        if (await db.Comptes.FindAsync(commande.CompteId) is not null)
            throw new RegleMetierException("Ce compte existe déjà.");

        db.Comptes.Add(Compte.Ouvrir(commande.CompteId, commande.Titulaire.Trim(), commande.DepotInitial,
            horloge.GetUtcNow().UtcDateTime));
        await db.Enregistrer();
    }
}
