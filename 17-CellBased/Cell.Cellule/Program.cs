using System.Net.Http.Json;
using Cell.Cellule;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

var idCellule = builder.Configuration["Cellule:Id"] ?? "c1";
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Magasin>();
builder.Services.AddSingleton<EtatCellule>();
builder.Services.AddSingleton(new VersionCellule(builder.Configuration["Cellule:Version"] ?? "1.0"));
// Pour les virements vers un client d'une autre cellule : la cellule ne connaît pas les autres cellules, elle passe par le routeur.
builder.Services.AddHttpClient("routeur", c => c.BaseAddress = new Uri(builder.Configuration["Routeur:Url"] ?? "http://localhost:5117"));

var app = builder.Build();

app.Use(async (http, suite) =>
{
    http.Response.Headers["X-Cellule"] = idCellule;
    http.Response.Headers["X-Version-Cellule"] = http.RequestServices.GetRequiredService<VersionCellule>().Courante;
    if (http.RequestServices.GetRequiredService<EtatCellule>().EnPanne && !http.Request.Path.StartsWithSegments("/admin"))
    {
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await http.Response.WriteAsJsonAsync(new { erreur = $"Cellule {idCellule} en panne." });
        return;
    }
    try
    {
        await suite(http);
    }
    catch (IntrouvableException e)
    {
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
    catch (RegleMetierException e)
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

// ─── L'API des clients (appelée uniquement à travers le routeur) ───
app.MapGet("/clients/{client}/comptes", (string client, Magasin magasin) => Results.Ok(magasin.Comptes(client)));

app.MapGet("/clients/{client}/comptes/{compte:guid}/mouvements", (string client, Guid compte, Magasin magasin) =>
    Results.Ok(magasin.Mouvements(client, compte)));

app.MapPost("/clients/{client}/comptes/{compte:guid}/retrait", (string client, Guid compte, Retrait r, Magasin magasin, VersionCellule version, TimeProvider horloge) =>
{
    if (r.Montant <= 0)
        throw new RegleMetierException("Le montant doit être positif.");
    var solde = magasin.Mouvementer(client, compte, s => version.Retirer(s, r.Montant), "Retrait", horloge.GetUtcNow());
    return Results.Ok(new { compte, solde });
});

app.MapPost("/clients/{client}/virements", async (string client, Virement v, Magasin magasin, IHttpClientFactory fabrique, TimeProvider horloge) =>
{
    if (v.Montant <= 0)
        throw new RegleMetierException("Le montant doit être positif.");

    // ① Les deux clients sont ici : une transaction locale, rapide et atomique. C'est le cas que le découpage doit favoriser.
    if (magasin.Heberge(v.ClientCible))
        return Results.Ok(new { interCellules = false, soldeSource = magasin.VirerLocalement(client, v.Source, v.ClientCible, v.Cible, v.Montant, horloge.GetUtcNow()) });

    // ② Le bénéficiaire est dans une autre cellule : débit local, puis crédit à distance via le routeur, idempotent.
    var operationId = Guid.NewGuid();
    var solde = magasin.Mouvementer(client, v.Source, s => s - v.Montant, $"Virement vers {v.ClientCible}", horloge.GetUtcNow(), operationId);
    HttpResponseMessage? reponse = null;
    try
    {
        reponse = await fabrique.CreateClient("routeur").PostAsJsonAsync($"/interne/clients/{v.ClientCible}/credits",
            new Credit(operationId, v.Cible, v.Montant, $"Virement de {client}"));
    }
    catch (HttpRequestException)
    {
        // le routeur lui-même est injoignable : traité comme une cellule cible injoignable
    }
    if (reponse is { IsSuccessStatusCode: true })
        return Results.Ok(new { interCellules = true, soldeSource = solde, operationId });

    // Pas de transaction entre deux cellules : on compense le débit.
    magasin.Mouvementer(client, v.Source, s => s + v.Montant, "Annulation : virement non abouti", horloge.GetUtcNow());
    return reponse?.StatusCode == System.Net.HttpStatusCode.NotFound
        ? Results.NotFound(new { erreur = $"Bénéficiaire introuvable chez {v.ClientCible} : virement annulé." })
        : Results.Json(new { erreur = $"La cellule de {v.ClientCible} est injoignable : virement annulé." }, statusCode: StatusCodes.Status502BadGateway);
});

// ─── API interne : appelée par le routeur et le plan de contrôle, jamais par les clients ───
app.MapPost("/interne/clients", (ClientExport client, Magasin magasin) =>
{
    magasin.Importer(client);
    return Results.Created($"/clients/{client.Id}/comptes", null);
});

app.MapGet("/interne/clients/{client}/export", (string client, Magasin magasin) => Results.Ok(magasin.Exporter(client)));

app.MapDelete("/interne/clients/{client}", (string client, Magasin magasin) =>
{
    magasin.Supprimer(client);
    return Results.NoContent();
});

app.MapPost("/interne/clients/{client}/credits", (string client, Credit c, Magasin magasin, TimeProvider horloge) =>
{
    var solde = magasin.Mouvementer(client, c.Compte, s => s + c.Montant, c.Libelle, horloge.GetUtcNow(), c.OperationId);
    return Results.Ok(new { compte = c.Compte, solde });
});

// ─── Exploitation de la cellule ───
app.MapGet("/sante", (Magasin magasin, VersionCellule version) =>
{
    var (sain, detail) = version.Sonde();
    return Results.Ok(new { cellule = idCellule, version = version.Courante, sain, detail, clients = magasin.NombreClients, historique = version.Historique });
});

app.MapPut("/admin/version", (ChangementVersion r, VersionCellule version) =>
{
    if (!VersionCellule.Connues.Contains(r.Version))
        return Results.BadRequest(new { erreur = $"Version inconnue. Connues : {string.Join(", ", VersionCellule.Connues)}." });
    version.Courante = r.Version;
    return Results.Ok(new { cellule = idCellule, version = version.Courante });
});

app.MapPut("/admin/panne", (Panne r, EtatCellule etat) =>
{
    etat.EnPanne = r.EnPanne;
    return Results.Ok(new { cellule = idCellule, enPanne = etat.EnPanne });
});

app.Run();

namespace Cell.Cellule
{
    public sealed record Retrait(decimal Montant);
    public sealed record Virement(Guid Source, string ClientCible, Guid Cible, decimal Montant);
    public sealed record Credit(Guid OperationId, Guid Compte, decimal Montant, string Libelle);
    public sealed record ChangementVersion(string Version);
    public sealed record Panne(bool EnPanne);
}
