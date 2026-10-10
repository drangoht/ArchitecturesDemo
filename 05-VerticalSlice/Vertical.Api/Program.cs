using Microsoft.EntityFrameworkCore;
using Vertical.Api.Data;
using Vertical.Api.Features.Comptes;
using Vertical.Api.Features.Virements;

var builder = WebApplication.CreateBuilder(args);

// Le nom de la base est lu à la demande : les tests d'intégration en donnent un différent à chaque application.
builder.Services.AddDbContext<BanqueDbContext>((services, options) =>
    options.UseInMemoryDatabase(services.GetRequiredService<IConfiguration>()["NomBase"] ?? "Banque"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<BanqueDbContext>().InitialiserDonneesDeDemo();

// Chaque tranche déclare elle-même sa route. Ajouter une fonctionnalité = un fichier + une ligne ici.
OuvrirCompte.Map(app);
ConsulterCompte.Map(app);
RetirerArgent.Map(app);
EffectuerVirement.Map(app);

app.Run();

// Rend Program visible pour WebApplicationFactory dans les tests d'intégration.
public partial class Program;
