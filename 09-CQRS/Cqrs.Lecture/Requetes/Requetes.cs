using Cqrs.Lecture.Modele;
using Microsoft.EntityFrameworkCore;

namespace Cqrs.Lecture.Requetes;

// Les gestionnaires de requêtes lisent les vues telles quelles : pas de domaine, pas de jointure, pas de calcul.

public record OperationVue(string Libelle, decimal Montant, decimal SoldeApres, DateTime Date);

// ─── Relevé d'un compte ───

public record ConsulterReleve(Guid CompteId) : IRequete<ReleveVue?>;

public record ReleveVue(Guid Id, string Titulaire, decimal Solde, int NombreOperations,
    DateTime DerniereOperationLe, IReadOnlyList<OperationVue> DernieresOperations);

public class ConsulterReleveGestionnaire(LectureDb db) : IGestionnaireRequete<ConsulterReleve, ReleveVue?>
{
    public async Task<ReleveVue?> Traiter(ConsulterReleve requete)
    {
        var releve = await db.Releves.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requete.CompteId);
        if (releve is null)
            return null;

        var dernieres = await db.Operations.AsNoTracking()
            .Where(o => o.CompteId == requete.CompteId)
            .OrderByDescending(o => o.Date)
            .Take(5)
            .Select(o => new OperationVue(o.Libelle, o.Montant, o.SoldeApres, o.Date))
            .ToListAsync();

        return new ReleveVue(releve.Id, releve.Titulaire, releve.Solde, releve.NombreOperations,
            releve.DerniereOperationLe, dernieres);
    }
}

// ─── Historique paginé ───

public record ListerOperations(Guid CompteId, int Page, int Taille) : IRequete<IReadOnlyList<OperationVue>>;

public class ListerOperationsGestionnaire(LectureDb db) : IGestionnaireRequete<ListerOperations, IReadOnlyList<OperationVue>>
{
    public async Task<IReadOnlyList<OperationVue>> Traiter(ListerOperations requete) =>
        await db.Operations.AsNoTracking()
            .Where(o => o.CompteId == requete.CompteId)
            .OrderByDescending(o => o.Date)
            .Skip((Math.Max(requete.Page, 1) - 1) * requete.Taille)
            .Take(Math.Clamp(requete.Taille, 1, 100))
            .Select(o => new OperationVue(o.Libelle, o.Montant, o.SoldeApres, o.Date))
            .ToListAsync();
}

// ─── Tableau de bord ───

public record ConsulterTableauDeBord : IRequete<TableauDeBordVue>;

public record TableauDeBordVue(int NombreComptes, decimal EncoursTotal, int NombreVirements, decimal MontantTotalVire);

public class ConsulterTableauDeBordGestionnaire(LectureDb db) : IGestionnaireRequete<ConsulterTableauDeBord, TableauDeBordVue>
{
    public async Task<TableauDeBordVue> Traiter(ConsulterTableauDeBord requete)
    {
        var s = await db.Statistiques.AsNoTracking().FirstOrDefaultAsync() ?? new Statistiques();
        return new TableauDeBordVue(s.NombreComptes, s.EncoursTotal, s.NombreVirements, s.MontantTotalVire);
    }
}
