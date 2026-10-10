using Ddd.Conformite;
using Ddd.TenueDeCompte.Application;
using Ddd.TenueDeCompte.Domaine;
using Ddd.TenueDeCompte.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenueDeCompte(sp => sp.GetRequiredService<IConfiguration>()["NomBase"] ?? "TenueDeCompte");
builder.Services.AddConformite();

var app = builder.Build();

// Comptes de démo : Alice et Bob en euros, Carla en dollars (pour voir la conversion).
using (var scope = app.Services.CreateScope())
{
    var comptes = scope.ServiceProvider.GetRequiredService<ServiceComptes>();
    await comptes.Ouvrir("Alice Martin", 50_000m, "EUR", DonneesDeDemo.Alice);
    await comptes.Ouvrir("Bob Durand", 500m, "EUR", DonneesDeDemo.Bob);
    await comptes.Ouvrir("Carla Smith", 1_000m, "USD", DonneesDeDemo.Carla);
}

app.Use(async (contexte, suivant) =>
{
    try
    {
        await suivant(contexte);
    }
    catch (RegleMetierException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status400BadRequest;
        await contexte.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
    catch (CompteIntrouvableException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status404NotFound;
        await contexte.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

// ─── Contexte « Tenue de compte » ───
app.MapGet("/comptes", (ServiceComptes comptes) => comptes.Lister());
app.MapGet("/comptes/{id:guid}", (Guid id, ServiceComptes comptes) => comptes.Consulter(id));
app.MapPost("/comptes", (OuvertureRequete r, ServiceComptes comptes) => comptes.Ouvrir(r.Titulaire, r.DepotInitial, r.Devise ?? "EUR"));
app.MapPost("/comptes/{id:guid}/retrait", (Guid id, RetraitRequete r, ServiceComptes comptes) => comptes.Retirer(id, r.Montant));
app.MapPost("/comptes/{id:guid}/virements", (Guid id, VirementRequete r, ServiceComptes comptes) =>
    comptes.Virer(id, r.IbanBeneficiaire, r.Montant));

// ─── Contexte « Conformité » ───
app.MapConformite();

app.Run();

record OuvertureRequete(string Titulaire, decimal DepotInitial, string? Devise);
record RetraitRequete(decimal Montant);
record VirementRequete(string IbanBeneficiaire, decimal Montant);

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");
}

public partial class Program;
