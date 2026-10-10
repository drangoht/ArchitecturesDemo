namespace Micro.Passerelle;

/// <summary>Type repère du service (utilisé par les tests pour démarrer CE service).</summary>
public sealed class Marqueur;

/// <summary>Reverse proxy minimal : transmet la requête au bon service et recopie sa réponse.</summary>
internal static class Relais
{
    public static async Task Transmettre(HttpContext contexte, IReadOnlyDictionary<string, string> routes, IHttpClientFactory http)
    {
        var chemin = contexte.Request.Path.Value ?? "/";
        var route = routes.FirstOrDefault(r => chemin.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase));
        if (route.Key is null)
        {
            contexte.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var requete = new HttpRequestMessage(
            new HttpMethod(contexte.Request.Method),
            route.Value + chemin + contexte.Request.QueryString);

        var aUnCorps = contexte.Request.ContentLength > 0
                       || contexte.Request.Headers.TransferEncoding.Count > 0
                       || contexte.Request.ContentType is not null;
        if (aUnCorps)
        {
            requete.Content = new StreamContent(contexte.Request.Body);
            if (contexte.Request.ContentType is { } type)
                requete.Content.Headers.TryAddWithoutValidation("Content-Type", type);
        }

        try
        {
            using var reponse = await http.CreateClient("aval")
                .SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, contexte.RequestAborted);

            contexte.Response.StatusCode = (int)reponse.StatusCode;
            if (reponse.Content.Headers.ContentType is { } typeReponse)
                contexte.Response.ContentType = typeReponse.ToString();
            await reponse.Content.CopyToAsync(contexte.Response.Body, contexte.RequestAborted);
        }
        catch (HttpRequestException)
        {
            contexte.Response.StatusCode = StatusCodes.Status502BadGateway;
            await contexte.Response.WriteAsJsonAsync(new { erreur = $"Le service derrière {route.Key} est injoignable." });
        }
    }
}
