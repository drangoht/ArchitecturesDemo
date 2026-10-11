using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Fig.Facade;
using Fig.Nouveau;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Fig.Tests;

/// <summary>Une horloge qu'on avance à la main : les deux systèmes datent leurs opérations avec elle.</summary>
public sealed class HorlogeReglable(DateTimeOffset depart) : TimeProvider
{
    private DateTimeOffset _maintenant = depart;

    public override DateTimeOffset GetUtcNow() => _maintenant;

    public void Regler(DateTimeOffset instant) => _maintenant = instant;

    public void Avancer(TimeSpan duree) => _maintenant += duree;
}

/// <summary>L'état d'une liaison HTTP entre deux processus : coupée, lente, ou qui ne laisse passer que les lectures.</summary>
public sealed class EtatLiaison
{
    public bool Coupee { get; set; }
    /// <summary>Les écritures échouent avant d'arriver (l'ancien tombe entre la lecture du journal et l'import).</summary>
    public bool EcrituresCoupees { get; set; }
    /// <summary>Les écritures arrivent et sont traitées, mais la réponse se perd en chemin.</summary>
    public bool ReponsesAuxEcrituresPerdues { get; set; }
    public TimeSpan Latence { get; set; }
}

public sealed class Liaison(EtatLiaison etat, HttpMessageHandler interieur) : DelegatingHandler(interieur)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
    {
        var ecriture = requete.Method != HttpMethod.Get;
        if (etat.Coupee || (ecriture && etat.EcrituresCoupees))
            throw new HttpRequestException("Liaison coupée (test)");
        if (etat.Latence > TimeSpan.Zero)
            await Task.Delay(etat.Latence, annulation);
        var reponse = await base.SendAsync(requete, annulation);
        if (ecriture && etat.ReponsesAuxEcrituresPerdues)
        {
            reponse.Dispose();   // la requête a été traitée, mais l'appelant n'en saura rien
            throw new HttpRequestException("Réponse perdue (test)");
        }
        return reponse;
    }
}

/// <summary>
/// Les trois processus de la démo, en mémoire : l'ancien système, le nouveau service (branché sur l'ancien) et la
/// façade (branchée sur les deux). Chaque liaison peut être coupée ou ralentie ; la synchronisation se déclenche à la main.
/// </summary>
public sealed class Systeme : IAsyncDisposable
{
    public const string Alice = "10001";
    public const string Bob = "10002";

    // Lundi 12 octobre 2026, 10 h à Paris (heure d'été).
    public HorlogeReglable Horloge { get; } = new(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero));

    public EtatLiaison NouveauVersAncien { get; } = new();
    public EtatLiaison FacadeVersAncien { get; } = new();
    public EtatLiaison FacadeVersNouveau { get; } = new();

    public WebApplicationFactory<Fig.Ancien.Marqueur> Ancien { get; }
    public WebApplicationFactory<Fig.Nouveau.Marqueur> Nouveau { get; }
    public WebApplicationFactory<Fig.Facade.Marqueur> Facade { get; }

    private HttpClient? _client, _admin, _ancienDirect, _nouveauDirect;

    public Systeme(int budgetOmbreMs = 500)
    {
        Ancien = new WebApplicationFactory<Fig.Ancien.Marqueur>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Horloge)));
        Nouveau = new WebApplicationFactory<Fig.Nouveau.Marqueur>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Synchronisation:IntervalleMs", "0");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<TimeProvider>(Horloge);
                s.AddHttpClient<ClientAncien>()
                    .ConfigurePrimaryHttpMessageHandler(() => new Liaison(NouveauVersAncien, Ancien.Server.CreateHandler()));
            });
        });
        Facade = new WebApplicationFactory<Fig.Facade.Marqueur>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Facade:BudgetOmbreMs", budgetOmbreMs.ToString());
            b.ConfigureTestServices(s =>
            {
                s.AddHttpClient("ancien").ConfigurePrimaryHttpMessageHandler(() => new Liaison(FacadeVersAncien, Ancien.Server.CreateHandler()));
                s.AddHttpClient("nouveau").ConfigurePrimaryHttpMessageHandler(() => new Liaison(FacadeVersNouveau, Nouveau.Server.CreateHandler()));
            });
        });
    }

    /// <summary>Un client : il ne connaît que la façade.</summary>
    public HttpClient Client => _client ??= Facade.CreateClient();

    public HttpClient Admin => _admin ??= CreerAdmin();

    public HttpClient AncienDirect => _ancienDirect ??= Ancien.CreateClient();

    public HttpClient NouveauDirect => _nouveauDirect ??= Nouveau.CreateClient();

    public Depot DepotDuNouveau => Nouveau.Services.GetRequiredService<Depot>();

    public Task<BilanSynchronisation> Synchroniser() => Nouveau.Services.GetRequiredService<Synchronisation>().Executer();

    public async Task Basculer(string route, Mode mode)
    {
        var reponse = await Admin.PutAsJsonAsync($"/facade/routes/{route}", new { mode = mode.ToString() });
        reponse.EnsureSuccessStatusCode();
    }

    public async Task ToutBasculer(Mode mode)
    {
        foreach (var route in new[] { "consultation", "operations", "releve", "virements" })
            await Basculer(route, mode);
    }

    /// <summary>Le traitement de nuit de l'ancien : il écrit sans passer par la façade.</summary>
    public async Task PreleverFrais(string numero, long centimes, string libelle)
    {
        var reponse = await AncienDirect.PostAsJsonAsync("/interne/frais", new Dictionary<string, object>
        {
            ["NUMCPT"] = numero, ["MNTCTS"] = centimes, ["LIBOPE"] = libelle
        });
        reponse.EnsureSuccessStatusCode();
    }

    public async Task<JsonNode> Json(HttpClient client, string chemin) => JsonNode.Parse(await client.GetStringAsync(chemin))!;

    public async Task<decimal> Solde(HttpClient client, string numero) =>
        (await Json(client, $"/comptes/{numero}"))["solde"]!.GetValue<decimal>();

    /// <summary>Les comparaisons du mode ombre se font après la réponse au client : on attend qu'elles soient faites.</summary>
    public async Task<JsonNode> AttendreComparaisons(Func<JsonNode, bool> condition)
    {
        var chrono = Stopwatch.StartNew();
        while (true)
        {
            var bilan = await Json(Admin, "/facade/comparaisons");
            if (condition(bilan))
                return bilan;
            if (chrono.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException($"Comparaisons attendues non obtenues : {bilan.ToJsonString()}");
            await Task.Delay(20);
        }
    }

    private HttpClient CreerAdmin()
    {
        var client = Facade.CreateClient();
        client.DefaultRequestHeaders.Add("X-Cle-Admin", "demo");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await Facade.DisposeAsync();
        await Nouveau.DisposeAsync();
        await Ancien.DisposeAsync();
    }
}
