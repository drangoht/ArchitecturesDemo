using Mk.Noyau;

namespace Mk.Hote;

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");
}

public sealed record DossierPlugins(string Chemin);

internal static class Endpoints
{
    internal record RetraitRequete(decimal Montant);
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);

    public static void MapBanque(this IEndpointRouteBuilder app)
    {
        app.MapGet("/comptes/{id:guid}", (Guid id, NoyauBancaire noyau) =>
            noyau.Consulter(id) is { } releve ? Results.Ok(releve) : Results.NotFound());

        app.MapPost("/comptes/{id:guid}/retrait", (Guid id, RetraitRequete r, NoyauBancaire noyau) =>
            EnHttp(noyau.Retirer(id, r.Montant)));

        app.MapPost("/virements", (VirementRequete r, NoyauBancaire noyau) =>
            EnHttp(noyau.Virer(r.Source, r.Cible, r.Montant)));

        // Le format est fourni par un plugin ; sans plugin de format, pas de relevé exportable.
        app.MapGet("/comptes/{id:guid}/releve", (Guid id, string format, NoyauBancaire noyau) =>
            noyau.Exporter(id, format, out var formats) is { } export
                ? Results.Text(export.Contenu, $"{export.TypeMime}; charset=utf-8")
                : Results.BadRequest(new { erreur = $"Format « {format} » indisponible.", formatsDisponibles = formats }));
    }

    /// <summary>Administration des plugins : lister, recharger, activer, désactiver — sans redémarrer.</summary>
    public static void MapAdministrationPlugins(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/plugins");

        admin.MapGet("", (RegistrePlugins registre) => registre.Tous.Select(p => new
        {
            p.Nom,
            p.Instance.Description,
            p.Source,
            Etat = p.Etat.ToString(),
            p.Erreur,
            p.PointsExtension
        }));

        admin.MapPost("/recharger", (ChargeurPlugins chargeur, DossierPlugins dossier, RegistrePlugins registre) =>
        {
            var decharges = chargeur.Recharger(dossier.Chemin);
            return Results.Ok(new { plugins = registre.Tous.Select(p => p.Nom), contextesDecharges = decharges.Count });
        });

        admin.MapPost("/{nom}/desactiver", (string nom, RegistrePlugins registre) =>
            registre.ChangerEtat(nom, actif: false) ? Results.NoContent() : Results.NotFound());

        admin.MapPost("/{nom}/activer", (string nom, RegistrePlugins registre) =>
            registre.ChangerEtat(nom, actif: true) ? Results.NoContent() : Results.NotFound());
    }

    private static IResult EnHttp(Resultat resultat) => resultat switch
    {
        { Succes: true } => Results.Ok(new { solde = resultat.Solde, frais = resultat.Frais }),
        { Introuvable: true } => Results.NotFound(new { erreur = resultat.Erreur }),
        _ => Results.BadRequest(new { erreur = resultat.Erreur })
    };
}

/// <summary>
/// Surveille le dossier des plugins : déposer, supprimer ou recompiler un plugin le recharge automatiquement.
/// Les événements sont regroupés (une compilation écrit plusieurs fichiers d'affilée).
/// </summary>
internal sealed class SurveillanceDesPlugins(
    ChargeurPlugins chargeur, DossierPlugins dossier, IConfiguration configuration, ILogger<SurveillanceDesPlugins> journal)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        if (!configuration.GetValue("Plugins:Surveiller", true) || !Directory.Exists(dossier.Chemin))
            return;

        using var surveillant = new FileSystemWatcher(dossier.Chemin)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        var dernierChangement = DateTime.MinValue;
        surveillant.Changed += (_, _) => dernierChangement = DateTime.UtcNow;
        surveillant.Created += (_, _) => dernierChangement = DateTime.UtcNow;
        surveillant.Deleted += (_, _) => dernierChangement = DateTime.UtcNow;
        surveillant.Renamed += (_, _) => dernierChangement = DateTime.UtcNow;
        surveillant.EnableRaisingEvents = true;
        journal.LogInformation("Surveillance de {Dossier}", dossier.Chemin);

        while (!arret.IsCancellationRequested)
        {
            await Task.Delay(250, arret);
            if (dernierChangement != DateTime.MinValue && DateTime.UtcNow - dernierChangement > TimeSpan.FromMilliseconds(750))
            {
                dernierChangement = DateTime.MinValue;
                journal.LogInformation("Changement détecté dans les plugins : rechargement.");
                chargeur.Recharger(dossier.Chemin);
            }
        }
    }
}
