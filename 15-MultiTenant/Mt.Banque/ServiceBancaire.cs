using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Mt.Locataires;

namespace Mt.Banque;

/// <summary>
/// Le métier, écrit comme pour une seule banque : pas un seul « where LocataireId = … ».
/// Le locataire n'intervient que pour lire SES règles (plafond, frais, quota de comptes).
/// </summary>
public sealed class ServiceBancaire(BanqueDbContext db, IContexteLocataire contexte, TimeProvider horloge)
{
    private Locataire Locataire => contexte.Exiger();
    private ReglesLocataire Regles => Locataire.Regles;
    private DateTimeOffset Maintenant => horloge.GetUtcNow();

    public async Task<IReadOnlyList<CompteDto>> Lister() =>
        await db.Comptes.OrderBy(c => c.Titulaire).Select(c => new CompteDto(c.Id, c.Titulaire, c.Solde)).ToListAsync();

    public async Task<CompteDto?> Consulter(Guid id) =>
        await db.Comptes.Where(c => c.Id == id).Select(c => new CompteDto(c.Id, c.Titulaire, c.Solde)).FirstOrDefaultAsync();

    public async Task<CompteDto> Ouvrir(string titulaire, decimal depotInitial)
    {
        if (string.IsNullOrWhiteSpace(titulaire) || depotInitial < 0)
            throw new RegleMetierException("Titulaire requis et dépôt initial positif ou nul.");
        // Quota de DONNÉES, propre au contrat du client (le quota de requêtes, lui, est géré par la plateforme).
        if (await db.Comptes.CountAsync() >= Regles.ComptesMax)
            throw new RegleMetierException($"{Locataire.Nom} a atteint son quota de {Regles.ComptesMax} comptes.");

        var compte = new Compte(Guid.NewGuid(), titulaire.Trim(), depotInitial);
        db.Comptes.Add(compte);
        db.Operations.Add(new Operation(compte.Id, Maintenant, "Dépôt initial", depotInitial));
        await db.SaveChangesAsync();
        return new CompteDto(compte.Id, compte.Titulaire, compte.Solde);
    }

    public async Task<CompteDto> Retirer(Guid id, decimal montant)
    {
        var compte = await Charger(id);
        compte.Debiter(montant);
        db.Operations.Add(new Operation(id, Maintenant, "Retrait", -montant));
        await db.SaveChangesAsync();
        return new CompteDto(compte.Id, compte.Titulaire, compte.Solde);
    }

    public async Task<VirementDto> Virer(Guid sourceId, Guid cibleId, decimal montant)
    {
        if (sourceId == cibleId)
            throw new RegleMetierException("Les comptes source et cible doivent être différents.");
        // Même code pour toutes les banques, avec les paramètres de chacune.
        if (montant > Regles.PlafondVirement)
            throw new RegleMetierException($"Plafond de virement de {Locataire.Nom} : {Regles.PlafondVirement:0.00} €.");

        // La cible est cherchée chez le locataire courant seulement : un virement vers un client d'une autre banque
        // de la plateforme est impossible par construction (ce serait un virement interbancaire, un autre processus).
        var source = await Charger(sourceId);
        var cible = await Charger(cibleId);
        var frais = Regles.FraisVirement;

        source.Debiter(montant + frais);
        cible.Crediter(montant);
        db.Operations.Add(new Operation(sourceId, Maintenant, $"Virement vers {cible.Titulaire}", -montant));
        db.Operations.Add(new Operation(cibleId, Maintenant, $"Virement de {source.Titulaire}", montant));
        if (frais > 0)
            db.Operations.Add(new Operation(sourceId, Maintenant, "Frais de virement", -frais));
        await db.SaveChangesAsync();
        return new VirementDto(sourceId, cibleId, montant, frais, source.Solde);
    }

    public async Task<IReadOnlyList<OperationDto>> Operations(Guid id)
    {
        await Charger(id);
        return await db.Operations.Where(o => o.CompteId == id).OrderBy(o => o.Date)
            .Select(o => new OperationDto(o.Date, o.Libelle, o.Montant)).ToListAsync();
    }

    /// <summary>
    /// Frais de tenue de compte du mois, selon le tarif de CE locataire. Idempotent : rejouer le même mois ne débite pas deux fois.
    /// Appelé par une tâche de la plateforme, hors de toute requête HTTP (voir ServicePlateforme).
    /// </summary>
    public async Task<int> PreleverFraisMensuels(string mois)
    {
        if (!DateTime.TryParseExact(mois, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new RegleMetierException("Mois attendu au format AAAA-MM.");
        var frais = Regles.FraisMensuels;
        if (frais <= 0)
            return 0;

        var cle = $"frais-{mois}";
        var dejaPreleves = await db.Operations.Where(o => o.Cle == cle).Select(o => o.CompteId).ToListAsync();
        var comptes = await db.Comptes.Where(c => !dejaPreleves.Contains(c.Id) && c.Solde > 0).ToListAsync();
        foreach (var compte in comptes)
        {
            var montant = Math.Min(frais, compte.Solde);
            compte.Debiter(montant);
            db.Operations.Add(new Operation(compte.Id, Maintenant, $"Frais de tenue de compte {mois}", -montant, cle));
        }
        await db.SaveChangesAsync();
        return comptes.Count;
    }

    private async Task<Compte> Charger(Guid id) =>
        await db.Comptes.FirstOrDefaultAsync(c => c.Id == id) ?? throw new CompteIntrouvableException(id);
}
