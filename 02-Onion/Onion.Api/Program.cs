using Onion.Application;
using Onion.Domain.Model;
using Onion.Domain.Services;
using Onion.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Composition root : chaque couche est branchée ici.
builder.Services.AddInfrastructure();
builder.Services.AddSingleton<ServiceVirement>();
builder.Services.AddScoped<ComptesAppService>();
builder.Services.AddScoped<VirementsAppService>();

var app = builder.Build();
await app.Services.InitialiserDonneesDeDemo();

// Les erreurs du domaine sont traduites en HTTP une seule fois, par un middleware.
app.Use(async (contexte, suivant) =>
{
    try
    {
        await suivant(contexte);
    }
    catch (CompteIntrouvableException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status404NotFound;
        await contexte.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
    catch (RegleMetierException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status400BadRequest;
        await contexte.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

app.MapGet("/comptes/{id:guid}", (Guid id, ComptesAppService comptes) => comptes.Consulter(id));

app.MapPost("/comptes/{id:guid}/retrait", (Guid id, RetraitRequete requete, ComptesAppService comptes) =>
    comptes.Retirer(id, requete.Montant));

app.MapPost("/virements", (VirementRequete requete, VirementsAppService virements) =>
    virements.Virer(requete.Source, requete.Cible, requete.Montant));

app.Run();

record RetraitRequete(decimal Montant);
record VirementRequete(Guid Source, Guid Cible, decimal Montant);
