using System.Globalization;
using Fig.Ancien;
using Microsoft.Extensions.DependencyInjection.Extensions;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SystemeBancaire>();

var app = builder.Build();

// Les erreurs métier, au format historique : {"erreur": "SOLDE INSUFFISANT"}.
app.Use(async (http, suite) =>
{
    try
    {
        await suite(http);
    }
    catch (ErreurMetier e)
    {
        http.Response.StatusCode = e.Statut;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

// ─── L'API publique historique : le CONTRAT dont dépendent les clients ───

app.MapGet("/comptes/{numero}", (string numero, SystemeBancaire banque) =>
{
    var compte = banque.Compte(numero);
    return Results.Ok(new { numero = compte.Numero, titulaire = compte.Titulaire, solde = compte.SoldeCentimes / 100m });
});

app.MapGet("/comptes/{numero}/operations", (string numero, SystemeBancaire banque) =>
    Results.Ok(banque.Operations(numero).Select(o => new
    {
        date = o.Date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        libelle = o.Libelle,
        montant = o.MontantCentimes / 100m
    })));

app.MapGet("/comptes/{numero}/releve", (string numero, string? mois, SystemeBancaire banque) =>
    Results.Text(banque.Releve(numero, mois), "text/plain; charset=utf-8"));

app.MapPost("/virements", (DemandeVirement d, SystemeBancaire banque) =>
{
    var (reference, solde) = banque.Virer(d.Source, d.Cible, d.Montant);
    return Results.Ok(new { reference, soldeSource = solde / 100m });
});

// Une fonction que personne n'a pensé à migrer : elle n'est pas dans la table de routage de la façade.
app.MapGet("/agences", () => Results.Ok(new[]
{
    new { code = "075", ville = "PARIS OPERA" },
    new { code = "069", ville = "LYON BELLECOUR" }
}));

// ─── Ajouté pour la migration, réservé au nouveau service (la façade ne le relaie jamais) ───

app.MapGet("/interne/journal", (long? depuis, int? max, SystemeBancaire banque) =>
    Results.Ok(banque.Journal(depuis ?? 0, Math.Clamp(max ?? 100, 1, 1_000))));

app.MapPost("/interne/operations-importees", (OperationImportee[] operations, SystemeBancaire banque) =>
    Results.Ok(new { importees = banque.Importer(operations) }));

// Le traitement de nuit (frais), déclenché à la main pour la démo.
app.MapPost("/interne/frais", (DemandeFrais frais, SystemeBancaire banque) =>
{
    banque.PreleverFrais(frais);
    return Results.NoContent();
});

app.Run();
