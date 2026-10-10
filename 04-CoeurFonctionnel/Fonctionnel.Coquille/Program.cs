using Fonctionnel.Coquille;
using Fonctionnel.Coquille.Donnees;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BanqueDbContext>(options => options.UseInMemoryDatabase("Banque"));
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<BanqueDbContext>().InitialiserDonneesDeDemo();

app.MapBanque();

app.Run();
