using Cqrs.Api;
using Cqrs.Ecriture;
using Cqrs.Ecriture.Commandes;
using Cqrs.Ecriture.Domaine;
using Cqrs.Evenements;
using Cqrs.Lecture;
using Cqrs.Lecture.Projections;
using Cqrs.Lecture.Requetes;

var builder = WebApplication.CreateBuilder(args);

string NomBase(IServiceProvider sp, string cote) =>
    $"{sp.GetRequiredService<IConfiguration>()["NomBase"] ?? "Banque"}-{cote}";

builder.Services.AddCoteEcriture(sp => NomBase(sp, "ecriture"));
builder.Services.AddCoteLecture(sp => NomBase(sp, "lecture"));
builder.Services.AddHostedService<SynchroniseurProjections>();

var app = builder.Build();

// Données de démo : elles passent, elles aussi, par des commandes.
using (var scope = app.Services.CreateScope())
{
    var bus = scope.ServiceProvider.GetRequiredService<BusCommandes>();
    await bus.Envoyer(new OuvrirCompte(DonneesDeDemo.Alice, "Alice", 1000m));
    await bus.Envoyer(new OuvrirCompte(DonneesDeDemo.Bob, "Bob", 500m));
}

app.Use(async (contexte, suivant) =>
{
    try
    {
        await suivant(contexte);
    }
    catch (CommandeInvalideException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status400BadRequest;
        await contexte.Response.WriteAsJsonAsync(new { erreurs = e.Erreurs });
    }
    catch (RegleMetierException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status400BadRequest;
        await contexte.Response.WriteAsJsonAsync(new { erreurs = new[] { e.Message } });
    }
    catch (CompteIntrouvableException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status404NotFound;
        await contexte.Response.WriteAsJsonAsync(new { erreurs = new[] { e.Message } });
    }
});

// ─── Commandes (écriture) : 202 Accepted, la lecture sera à jour sous peu ───

app.MapPost("/comptes", async (OuvertureRequete r, BusCommandes bus) =>
{
    var id = Guid.NewGuid();
    await bus.Envoyer(new OuvrirCompte(id, r.Titulaire, r.DepotInitial));
    return Results.Accepted($"/comptes/{id}", new { id });
});

app.MapPost("/comptes/{id:guid}/retrait", async (Guid id, RetraitRequete r, BusCommandes bus) =>
{
    await bus.Envoyer(new RetirerArgent(id, r.Montant));
    return Results.Accepted($"/comptes/{id}");
});

app.MapPost("/virements", async (VirementRequete r, BusCommandes bus) =>
{
    var virementId = Guid.NewGuid();
    await bus.Envoyer(new EffectuerVirement(virementId, r.Source, r.Cible, r.Montant));
    return Results.Accepted($"/comptes/{r.Source}", new { virementId });
});

// ─── Requêtes (lecture) : servies par les modèles de lecture, sans passer par le domaine ───

app.MapGet("/comptes/{id:guid}", async (Guid id, BusRequetes bus) =>
    await bus.Demander(new ConsulterReleve(id)) is { } releve ? Results.Ok(releve) : Results.NotFound());

app.MapGet("/comptes/{id:guid}/operations", (Guid id, int? page, int? taille, BusRequetes bus) =>
    bus.Demander(new ListerOperations(id, page ?? 1, taille ?? 20)));

app.MapGet("/tableau-de-bord", (BusRequetes bus) => bus.Demander(new ConsulterTableauDeBord()));

// Observabilité : de combien d'événements la lecture est-elle en retard sur l'écriture ?
app.MapGet("/admin/projections", async (IFluxEvenements flux, Projecteur projecteur) =>
{
    var ecrit = await flux.DerniereSequence();
    var projete = await projecteur.DerniereSequence();
    return Results.Ok(new { dernierEvenementEcrit = ecrit, dernierEvenementProjete = projete, retard = ecrit - projete });
});

app.Run();

record OuvertureRequete(string Titulaire, decimal DepotInitial);
record RetraitRequete(decimal Montant);
record VirementRequete(Guid Source, Guid Cible, decimal Montant);

public partial class Program;
