using Saga.Participants;

namespace Saga.Orchestrateur;

public abstract record Issue
{
    public sealed record Reussie : Issue;
    public sealed record EnAttente(TimeSpan Delai, string Motif) : Issue;
    public sealed record Refusee(string Raison) : Issue;

    public static readonly Issue Succes = new Reussie();
}

/// <param name="Compenser">L'action qui défait l'étape (null : rien à défaire).</param>
/// <param name="PointDeNonRetour">Une fois cette étape faite, on ne compense plus : on ne peut qu'aller au bout.</param>
public sealed record Etape(
    string Nom,
    Func<EtatSaga, Task<Issue>> Executer,
    Func<EtatSaga, Task>? Compenser = null,
    bool PointDeNonRetour = false);

public sealed class OptionsSaga
{
    public int TentativesMax { get; set; } = 4;
    public int AttenteInitialeMs { get; set; } = 200;
    public int PauseEntreEtapesMs { get; set; } = 300;
    public int DelaiDecisionConformiteSecondes { get; set; } = 60;
}

/// <summary>
/// LA DÉFINITION DU PROCESSUS, lisible d'un coup d'œil : c'est le grand avantage de l'orchestration.
/// En chorégraphie (démos 7 et 8), cette logique est éparpillée dans les réactions de chaque service.
/// </summary>
public sealed class DefinitionVirement(
    ServiceComptes comptes, ServiceConformite conformite, ServiceChange change, ServiceNotifications notifications, OptionsSaga options)
{
    public IReadOnlyList<Etape> Etapes { get; } =
    [
        new("ReserverFonds",
            async s => { await comptes.Reserver(s.Id, s.Source, s.Montant); return Issue.Succes; },
            Compenser: s => comptes.Liberer(s.Id, s.Source)),

        new("ControlerConformite", async s =>
            await conformite.Examiner(s.Id, ServiceChange.EnEuros(s.Montant, comptes.Devise(s.Source))) switch
            {
                AvisConformite.Approuve => Issue.Succes,
                AvisConformite.EnAttenteAnalyste => new Issue.EnAttente(
                    TimeSpan.FromSeconds(options.DelaiDecisionConformiteSecondes), "en attente de la décision d'un analyste"),
                _ => new Issue.Refusee("Refusé par la conformité.")
            }),

        new("ConvertirDevises",
            async s =>
            {
                var (de, vers) = (comptes.Devise(s.Source), comptes.Devise(s.Cible));
                s.DeviseCible = vers;
                s.MontantCredite = de == vers ? s.Montant : await change.Convertir(s.Id, s.Montant, de, vers);
                return Issue.Succes;
            },
            Compenser: s => change.AnnulerCotation(s.Id)),

        new("CrediterBeneficiaire",
            async s => { await comptes.Crediter(s.Id, s.Cible, s.MontantCredite!.Value); return Issue.Succes; },
            Compenser: s => comptes.AnnulerCredit(s.Id, s.Cible)),

        new("DebiterDefinitivement",
            async s => { await comptes.Capturer(s.Id, s.Source); return Issue.Succes; },
            PointDeNonRetour: true),

        new("Notifier", async s =>
        {
            await notifications.Envoyer(s.Id,
                $"Virement de {s.Montant:0.00} {comptes.Devise(s.Source)} effectué ({s.MontantCredite:0.00} {s.DeviseCible} crédités).");
            return Issue.Succes;
        })
    ];
}
