using Composants.Comptes;
using Composants.Virements;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddComposantComptes();
builder.Services.AddComposantVirements();

var app = builder.Build();

// Même l'initialisation passe par l'interface publique du composant.
using (var scope = app.Services.CreateScope())
{
    var comptes = scope.ServiceProvider.GetRequiredService<IComptesComposant>();
    await comptes.Ouvrir("Alice", 1000m, Guid.Parse("11111111-1111-1111-1111-111111111111"));
    await comptes.Ouvrir("Bob", 500m, Guid.Parse("22222222-2222-2222-2222-222222222222"));
}

// Essayez d'écrire « new ComptesDbContext(...) » ou « new Compte(...) » ici : ça ne compile pas.

app.MapGet("/comptes/{id:guid}", async (Guid id, IComptesComposant comptes) =>
    await comptes.Consulter(id) is { } compte ? Results.Ok(compte) : Results.NotFound());

app.MapPost("/comptes/{id:guid}/retrait", async (Guid id, RetraitRequete requete, IComptesComposant comptes) =>
    EnHttp(await comptes.Retirer(id, requete.Montant)));

app.MapPost("/virements", async (VirementRequete requete, IVirementsComposant virements) =>
    EnHttp(await virements.Virer(requete.Source, requete.Cible, requete.Montant)));

app.MapGet("/comptes/{id:guid}/virements", (Guid id, IVirementsComposant virements) =>
    virements.Historique(id));

app.Run();

static IResult EnHttp(Resultat resultat) => resultat.Statut switch
{
    StatutResultat.Ok => Results.NoContent(),
    StatutResultat.Introuvable => Results.NotFound(new { erreur = resultat.Erreur }),
    _ => Results.BadRequest(new { erreur = resultat.Erreur })
};

record RetraitRequete(decimal Montant);
record VirementRequete(Guid Source, Guid Cible, decimal Montant);
