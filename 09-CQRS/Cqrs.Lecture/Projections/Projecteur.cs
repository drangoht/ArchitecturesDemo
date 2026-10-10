using Cqrs.Evenements;
using Cqrs.Lecture.Modele;

namespace Cqrs.Lecture.Projections;

/// <summary>
/// Transforme les événements du domaine en modèles de lecture. C'est le seul code qui écrit dans la base de lecture.
/// On pourrait jeter toute la base de lecture et la reconstruire en rejouant les événements depuis le début.
/// </summary>
public class Projecteur(LectureDb db)
{
    public async Task<long> DerniereSequence() => (await ObtenirPointDeReprise()).DerniereSequence;

    public async Task Appliquer(EvenementEnregistre enregistre)
    {
        var point = await ObtenirPointDeReprise();
        if (enregistre.Sequence <= point.DerniereSequence)
            return;  // déjà projeté : rejouer un événement n'a pas d'effet

        var stats = await db.Statistiques.FindAsync(1) ?? db.Statistiques.Add(new Statistiques()).Entity;

        switch (enregistre.Evenement)
        {
            case CompteOuvert e:
                db.Releves.Add(new ReleveCompte
                {
                    Id = e.CompteId, Titulaire = e.Titulaire, Solde = e.DepotInitial,
                    NombreOperations = 1, DerniereOperationLe = e.Date
                });
                AjouterLigne(e.CompteId, "Ouverture du compte", e.DepotInitial, e.DepotInitial, e.Date);
                stats.NombreComptes++;
                stats.EncoursTotal += e.DepotInitial;
                break;

            case ArgentRetire e:
                await MettreAJour(e.CompteId, e.NouveauSolde, e.Date);
                AjouterLigne(e.CompteId, "Retrait", -e.Montant, e.NouveauSolde, e.Date);
                stats.EncoursTotal -= e.Montant;
                break;

            case VirementEffectue e:
                var source = await MettreAJour(e.SourceId, e.SoldeSource, e.Date);
                var cible = await MettreAJour(e.CibleId, e.SoldeCible, e.Date);
                // Le libellé est rédigé une fois pour toutes, avec le nom de l'autre titulaire.
                AjouterLigne(e.SourceId, $"Virement vers {cible.Titulaire}", -e.Montant, e.SoldeSource, e.Date);
                AjouterLigne(e.CibleId, $"Virement reçu de {source.Titulaire}", e.Montant, e.SoldeCible, e.Date);
                stats.NombreVirements++;
                stats.MontantTotalVire += e.Montant;
                break;
        }

        point.DerniereSequence = enregistre.Sequence;
        await db.SaveChangesAsync();
    }

    private async Task<ReleveCompte> MettreAJour(Guid compteId, decimal solde, DateTime date)
    {
        var releve = (await db.Releves.FindAsync(compteId))!;
        releve.Solde = solde;
        releve.NombreOperations++;
        releve.DerniereOperationLe = date;
        return releve;
    }

    private void AjouterLigne(Guid compteId, string libelle, decimal montant, decimal soldeApres, DateTime date) =>
        db.Operations.Add(new LigneOperation
        {
            CompteId = compteId, Libelle = libelle, Montant = montant, SoldeApres = soldeApres, Date = date
        });

    private async Task<PointDeReprise> ObtenirPointDeReprise() =>
        await db.PointsDeReprise.FindAsync(1) ?? db.PointsDeReprise.Add(new PointDeReprise()).Entity;
}
