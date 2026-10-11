using System.Text;
using System.Text.Json.Nodes;

namespace Fig.Facade;

/// <summary>Une réponse lue en entier, pour pouvoir la renvoyer au client ET la comparer.</summary>
public sealed record ReponseLue(int Statut, string? TypeContenu, byte[] Corps, List<KeyValuePair<string, string[]>> EnTetes)
{
    public string Texte => Encoding.UTF8.GetString(Corps);
}

/// <summary>
/// Le relais : retrouve la route, l'envoie au système qui en a la charge et recopie sa réponse. Aucune logique métier :
/// la façade ne sait rien des comptes ni des virements, seulement qui répond à quoi.
/// </summary>
public sealed class Relais(IHttpClientFactory fabrique, TableDeRoutage table, Comparateur comparateur, OptionsFacade options)
{
    private static readonly HashSet<string> NonRelayes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection", "Content-Length"
    };

    public async Task Traiter(HttpContext http)
    {
        var chemin = http.Request.Path.Value ?? "/";
        // Les routes ajoutées pour la migration (journal, import, frais) ne regardent que les deux systèmes.
        if (chemin.StartsWith("/interne", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var route = table.Trouver(http.Request.Method, chemin);
        var mode = route?.Mode ?? Mode.Ancien;   // ce que personne n'a repris reste à l'ancien : c'est le point de départ
        table.Compter(route?.Nom ?? TableDeRoutage.NonDeclaree, parLAncien: mode != Mode.Nouveau);
        var corps = await LireCorps(http.Request);
        var systeme = mode == Mode.Nouveau ? "nouveau" : "ancien";

        ReponseLue reponse;
        if (mode == Mode.Ombre)
        {
            // Les deux appels partent en même temps ; le client n'attend que l'ancien.
            var budget = new CancellationTokenSource(options.BudgetOmbreMs);
            var ombre = Envoyer("nouveau", Message(http.Request, corps), budget.Token);
            try
            {
                reponse = await Envoyer("ancien", Message(http.Request, corps), http.RequestAborted);
            }
            catch (Exception e)
            {
                _ = comparateur.Abandonner(ombre, budget);
                if (e is not HttpRequestException)
                    throw;
                await Injoignable(http, systeme);
                return;
            }
            // Comparée APRÈS coup, sans être attendue : un nouveau service lent ou en panne ne ralentit personne.
            _ = comparateur.Comparer(route!.Nom, $"{http.Request.Method} {chemin}{http.Request.QueryString}", reponse, ombre, budget);
        }
        else
        {
            try
            {
                reponse = await Envoyer(systeme, Message(http.Request, corps), http.RequestAborted);
            }
            catch (HttpRequestException)
            {
                await Injoignable(http, systeme);
                return;
            }
        }

        http.Response.StatusCode = reponse.Statut;
        foreach (var (nom, valeurs) in reponse.EnTetes.Where(e => !NonRelayes.Contains(e.Key)))
            http.Response.Headers[nom] = valeurs;
        http.Response.Headers["X-Servi-Par"] = systeme;
        await http.Response.Body.WriteAsync(reponse.Corps, http.RequestAborted);
    }

    // Le message est construit tout de suite, pendant que la requête du client existe encore : l'appel en ombre lui survit.
    private static HttpRequestMessage Message(HttpRequest requete, byte[]? corps)
    {
        var message = new HttpRequestMessage(new HttpMethod(requete.Method), $"{requete.Path}{requete.QueryString}");
        if (corps is not null)
            message.Content = new ByteArrayContent(corps);
        foreach (var (nom, valeurs) in requete.Headers.Where(e => !NonRelayes.Contains(e.Key)))
            if (!message.Headers.TryAddWithoutValidation(nom, valeurs.ToArray()))
                message.Content?.Headers.TryAddWithoutValidation(nom, valeurs.ToArray());
        return message;
    }

    private async Task<ReponseLue> Envoyer(string systeme, HttpRequestMessage message, CancellationToken annulation)
    {
        using (message)
        using (var reponse = await fabrique.CreateClient(systeme).SendAsync(message, annulation))
        {
            var corps = await reponse.Content.ReadAsByteArrayAsync(annulation);
            var enTetes = reponse.Headers.Concat(reponse.Content.Headers).Select(e => KeyValuePair.Create(e.Key, e.Value.ToArray())).ToList();
            return new ReponseLue((int)reponse.StatusCode, reponse.Content.Headers.ContentType?.MediaType, corps, enTetes);
        }
    }

    // Le corps est lu en entier (il peut devoir partir deux fois), qu'il ait une longueur annoncée ou qu'il arrive par morceaux.
    private static async Task<byte[]?> LireCorps(HttpRequest requete)
    {
        using var tampon = new MemoryStream();
        await requete.Body.CopyToAsync(tampon, requete.HttpContext.RequestAborted);
        return tampon.Length == 0 ? null : tampon.ToArray();
    }

    private static Task Injoignable(HttpContext http, string systeme)
    {
        http.Response.StatusCode = StatusCodes.Status502BadGateway;
        http.Response.Headers["X-Servi-Par"] = systeme;
        return http.Response.WriteAsJsonAsync(new { erreur = $"Système {systeme} injoignable." });
    }
}

public sealed record Comparaison(DateTimeOffset Date, string Route, string Requete, string Resultat, string? Raison,
    int StatutAncien, int? StatutNouveau, string? CorpsAncien, string? CorpsNouveau);

/// <summary>
/// Le mode ombre : la réponse de l'ancien fait foi, celle du nouveau est comparée en silence. On accumule ainsi la
/// preuve, sur du trafic réel, que le nouveau répond pareil, AVANT de lui confier la route.
/// </summary>
public sealed class Comparateur(ILogger<Comparateur> journal)
{
    private const int Conservees = 20;
    private readonly object _verrou = new();
    private readonly LinkedList<Comparaison> _dernieresDivergences = new();
    private long _identiques, _divergences, _abandonnees;

    public async Task Comparer(string route, string requete, ReponseLue ancien, Task<ReponseLue> ombre, CancellationTokenSource budget)
    {
        using (budget)
        {
            ReponseLue nouveau;
            try
            {
                nouveau = await ombre;
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
            {
                // Trop lent ou en panne : ce n'est pas une divergence, mais on le note (le nouveau doit aussi tenir la charge).
                Interlocked.Increment(ref _abandonnees);
                journal.LogInformation("Ombre abandonnée pour {Requete} : {Raison}", requete, e is OperationCanceledException ? "budget dépassé" : e.Message);
                return;
            }

            var raison = Difference(ancien, nouveau);
            if (raison is null)
            {
                Interlocked.Increment(ref _identiques);
                return;
            }
            Interlocked.Increment(ref _divergences);
            journal.LogWarning("DIVERGENCE {Route} ({Requete}) : {Raison}", route, requete, raison);
            lock (_verrou)
            {
                _dernieresDivergences.AddFirst(new Comparaison(DateTimeOffset.UtcNow, route, requete, "divergence", raison,
                    ancien.Statut, nouveau.Statut, Tronquer(ancien.Texte), Tronquer(nouveau.Texte)));
                if (_dernieresDivergences.Count > Conservees)
                    _dernieresDivergences.RemoveLast();
            }
        }
    }

    public async Task Abandonner(Task<ReponseLue> ombre, CancellationTokenSource budget)
    {
        using (budget)
        {
            try { await ombre; }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
        }
    }

    public object Bilan()
    {
        lock (_verrou)
        {
            var identiques = Interlocked.Read(ref _identiques);
            var divergences = Interlocked.Read(ref _divergences);
            return new
            {
                comparees = identiques + divergences,
                identiques,
                divergences,
                abandonnees = Interlocked.Read(ref _abandonnees),
                dernieresDivergences = _dernieresDivergences.ToList()
            };
        }
    }

    /// <summary>Même statut et même contenu. Le JSON est comparé en structure (espaces, ordre des propriétés : sans importance).</summary>
    public static string? Difference(ReponseLue ancien, ReponseLue nouveau)
    {
        if (ancien.Statut != nouveau.Statut)
            return $"statut {ancien.Statut} ≠ {nouveau.Statut}";
        if (ancien.TypeContenu != nouveau.TypeContenu)
            return $"type de contenu {ancien.TypeContenu} ≠ {nouveau.TypeContenu}";
        if (ancien.TypeContenu?.Contains("json") == true && ancien.Corps.Length > 0 && nouveau.Corps.Length > 0)
            return JsonNode.DeepEquals(JsonNode.Parse(ancien.Corps), JsonNode.Parse(nouveau.Corps)) ? null : "contenu JSON différent";
        return ancien.Corps.AsSpan().SequenceEqual(nouveau.Corps) ? null : "contenu différent";
    }

    private static string Tronquer(string texte) => texte.Length <= 500 ? texte : texte[..500] + "…";
}
