using Saga.Orchestrateur;
using Saga.Participants;

namespace Saga.Api;

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");
}

/// <summary>
/// Fait tourner le moteur en continu. On peut l'arrêter et le relancer pour constater que les sagas
/// reprennent exactement là où elles en étaient (leur état est dans le magasin, pas dans le moteur).
/// </summary>
public sealed class BoucleOrchestrateur(MoteurSaga moteur, ILogger<BoucleOrchestrateur> journal) : BackgroundService
{
    public bool EnPause { get; set; }

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        while (!arret.IsCancellationRequested)
        {
            if (!EnPause)
            {
                try
                {
                    await moteur.Traiter();
                }
                catch (Exception e)
                {
                    journal.LogError(e, "Erreur inattendue du moteur");
                }
            }
            await Task.Delay(100, arret);
        }
    }
}

internal static class Endpoints
{
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);
    internal record DecisionRequete(bool Approuve, string? Commentaire);
    internal record ChaosRequete(int? EchecsChangeRestants, bool? ChangeHorsService, bool? NotificationsHorsService);

    public static void MapSagas(this IEndpointRouteBuilder app)
    {
        // ─── Démarrer et suivre une saga ───
        app.MapPost("/virements", (VirementRequete r, MoteurSaga moteur) =>
        {
            if (r.Montant <= 0 || r.Source == r.Cible)
                return Results.BadRequest(new { erreur = "Montant positif et comptes différents requis." });
            var saga = moteur.Demarrer(r.Source, r.Cible, r.Montant);
            return Results.Accepted($"/sagas/{saga.Id}", new { sagaId = saga.Id });
        });

        app.MapGet("/sagas/{id:guid}", (Guid id, MagasinSagas magasin, MoteurSaga moteur) =>
            magasin.Charger(id) is { } saga ? Results.Ok(Vue(saga, moteur)) : Results.NotFound());

        app.MapGet("/sagas", (MagasinSagas magasin) => magasin.Toutes()
            .Select(s => new { s.Id, Statut = s.Statut.ToString(), s.Montant, s.Raison, Pas = s.Journal.Count }));

        // ─── Événement extérieur : la décision d'un analyste de conformité ───
        app.MapPost("/sagas/{id:guid}/conformite", async (Guid id, DecisionRequete d, MoteurSaga moteur) =>
            await moteur.Signaler(id, d.Approuve, d.Commentaire)
                ? Results.NoContent()
                : Results.Conflict(new { erreur = "Cette saga n'attend pas de décision." }));

        // ─── Les participants (en réalité, d'autres services) ───
        app.MapGet("/comptes", (ServiceComptes comptes) => comptes.Tous());
        app.MapGet("/notifications", (ServiceNotifications notifications) => notifications.Envoyees);

        // ─── Démo : provoquer des pannes, arrêter / relancer l'orchestrateur ───
        app.MapPut("/chaos", (ChaosRequete r, Chaos chaos) =>
        {
            chaos.EchecsChangeRestants = r.EchecsChangeRestants ?? chaos.EchecsChangeRestants;
            chaos.ChangeHorsService = r.ChangeHorsService ?? chaos.ChangeHorsService;
            chaos.NotificationsHorsService = r.NotificationsHorsService ?? chaos.NotificationsHorsService;
            return Results.Ok(chaos);
        });
        app.MapPost("/admin/orchestrateur/arreter", (BoucleOrchestrateur boucle) => { boucle.EnPause = true; return Results.NoContent(); });
        app.MapPost("/admin/orchestrateur/relancer", (BoucleOrchestrateur boucle) => { boucle.EnPause = false; return Results.NoContent(); });
    }

    /// <summary>Vue de suivi : l'état de chaque étape, déduit de l'état enregistré de la saga.</summary>
    private static object Vue(EtatSaga saga, MoteurSaga moteur) => new
    {
        saga.Id,
        Statut = saga.Statut.ToString(),
        saga.Montant,
        saga.MontantCredite,
        saga.DeviseCible,
        saga.Raison,
        saga.Echeance,
        Etapes = moteur.NomsDesEtapes.Select((nom, i) => new
        {
            nom,
            etat = saga.EtapesFaites.Contains(nom) ? "faite"
                : saga.Statut is StatutSaga.Compensation or StatutSaga.Annulee && i < saga.EtapeCourante ? "défaite"
                : i == saga.EtapeCourante && !saga.EstFinie ? saga.Statut.ToString()
                : "—"
        }),
        Journal = saga.Journal.Select(j => $"{j.Date:HH:mm:ss.fff} {j.Message}")
    };
}
