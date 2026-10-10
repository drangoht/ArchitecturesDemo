using System.Collections.Concurrent;

namespace Ddd.Conformite;

public enum NiveauAlerte { Elevee, Critique }

public record Alerte(Guid Id, Guid CompteId, string Titulaire, NiveauAlerte Niveau, string Motif, DateTime Date);

/// <summary>Objet-valeur de CE contexte : la Conformité raisonne en euros, la devise d'origine ne l'intéresse pas.</summary>
public record VirementObserve(DateTime Date, decimal MontantEnEuros, string IbanBeneficiaire);

/// <summary>
/// Agrégat du contexte Conformité. Il représente le même compte que la Tenue de compte,
/// mais avec un tout autre modèle : pas de solde, pas de plafond, des virements observés et des alertes.
/// </summary>
public class DossierDeVigilance(Guid compteId, string titulaire)
{
    public const decimal SeuilDeDeclaration = 10_000m;
    public static readonly TimeSpan FenetreDeFractionnement = TimeSpan.FromMinutes(10);
    public const int VirementsMaxDansLaFenetre = 3;

    private readonly List<VirementObserve> _virements = [];
    private readonly List<Alerte> _alertes = [];

    public Guid CompteId { get; } = compteId;
    public string Titulaire { get; } = titulaire;
    public bool BlocageDemande { get; private set; }
    public IReadOnlyList<VirementObserve> Virements => _virements;
    public IReadOnlyList<Alerte> Alertes => _alertes;

    /// <summary>Enregistre un virement et renvoie les alertes qu'il déclenche.</summary>
    public IReadOnlyList<Alerte> Observer(VirementObserve virement)
    {
        _virements.Add(virement);
        var nouvelles = new List<Alerte>();

        if (virement.MontantEnEuros >= SeuilDeDeclaration)
            nouvelles.Add(NouvelleAlerte(NiveauAlerte.Elevee, virement.Date,
                $"Virement de {virement.MontantEnEuros:N2} € au-delà du seuil de déclaration ({SeuilDeDeclaration:N0} €)."));

        var rapproches = _virements.Count(v => v.Date > virement.Date - FenetreDeFractionnement && v.Date <= virement.Date);
        if (rapproches >= VirementsMaxDansLaFenetre && !BlocageDemande)
        {
            nouvelles.Add(NouvelleAlerte(NiveauAlerte.Critique, virement.Date,
                $"{rapproches} virements en moins de {FenetreDeFractionnement.TotalMinutes:0} minutes : fractionnement suspecté."));
            BlocageDemande = true;
        }

        _alertes.AddRange(nouvelles);
        return nouvelles;
    }

    private Alerte NouvelleAlerte(NiveauAlerte niveau, DateTime date, string motif) =>
        new(Guid.NewGuid(), CompteId, Titulaire, niveau, motif, date);
}

/// <summary>Le stockage propre à la Conformité (en mémoire pour la démo). Il ne partage rien avec la Tenue de compte.</summary>
public class DossiersDeVigilance
{
    private readonly ConcurrentDictionary<Guid, DossierDeVigilance> _dossiers = new();

    public DossierDeVigilance Obtenir(Guid compteId, string titulaire) =>
        _dossiers.GetOrAdd(compteId, id => new DossierDeVigilance(id, titulaire));

    public DossierDeVigilance? Trouver(Guid compteId) => _dossiers.GetValueOrDefault(compteId);

    public IEnumerable<DossierDeVigilance> Tous() => _dossiers.Values;
}
