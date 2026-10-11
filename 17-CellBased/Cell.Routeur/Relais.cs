namespace Cell.Routeur;

/// <summary>
/// LE PLAN DE DONNÉES : lire la clé de partition, trouver la cellule, relayer. Rien d'autre : pas de métier, pas de base,
/// pas d'agrégation. Le routeur est le seul composant partagé par toutes les cellules ; plus il est simple, moins il risque
/// de tomber (et de tout entraîner avec lui).
/// </summary>
public sealed class Relais(TablePlacement placement, RegistreCellules registre, IHttpClientFactory fabrique)
{
    public async Task<IResult> Relayer(string client, HttpContext http)
    {
        if (placement.CelluleDe(client) is not { } id || registre.Trouver(id) is not { } cellule)
            return Results.NotFound(new { erreur = $"Client « {client} » inconnu." });
        if (placement.EnMigration(client))
        {
            http.Response.Headers.RetryAfter = "2";
            return Results.Json(new { erreur = $"Le client « {client} » change de cellule : réessayer dans un instant." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        using var requete = new HttpRequestMessage(new HttpMethod(http.Request.Method),
            new Uri(new Uri(cellule.Url), http.Request.Path + http.Request.QueryString));
        // Corps présent : longueur connue, ou envoi par morceaux (JSON sérialisé à la volée, sans Content-Length).
        var methode = http.Request.Method;
        if (http.Request.ContentLength > 0 || http.Request.ContentLength is null && (HttpMethods.IsPost(methode) || HttpMethods.IsPut(methode) || HttpMethods.IsPatch(methode)))
        {
            requete.Content = new StreamContent(http.Request.Body);
            if (http.Request.ContentType is { } type)
                requete.Content.Headers.TryAddWithoutValidation("Content-Type", type);
        }

        HttpResponseMessage reponse;
        try
        {
            reponse = await fabrique.CreateClient($"cellule-{cellule.Id}").SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, http.RequestAborted);
        }
        catch (HttpRequestException)
        {
            // Rayon d'impact : seuls les clients de CETTE cellule reçoivent cette erreur.
            http.Response.Headers["X-Cellule"] = cellule.Id;
            return Results.Json(new { erreur = $"Cellule {cellule.Id} injoignable : seuls ses clients sont touchés." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        using (reponse)
        {
            http.Response.StatusCode = (int)reponse.StatusCode;
            foreach (var entete in new[] { "X-Cellule", "X-Version-Cellule", "Retry-After" })
                if (reponse.Headers.TryGetValues(entete, out var valeurs))
                    http.Response.Headers[entete] = valeurs.ToArray();
            if (reponse.Content.Headers.ContentType is { } contenu)
                http.Response.ContentType = contenu.ToString();
            await reponse.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
        }
        return Results.Empty;
    }
}
