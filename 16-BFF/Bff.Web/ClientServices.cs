using System.Diagnostics;
using System.Net.Http.Json;

namespace Bff.Web;

// Les contrats des services, recopiés ici avec SEULEMENT les champs dont cet écran a besoin (« lecteur tolérant ») :
// un champ ajouté ou retiré ailleurs dans les services ne casse pas ce BFF.
public sealed record Connexion(string Utilisateur, string MotDePasse);
public sealed record VirementRequete(Guid Source, Guid Cible, decimal Montant);

public sealed record JetonAval(string Jeton, int ExpireDans);
public sealed record ProfilAval(string Prenom, string Nom, string Email, string Segment, string ConseillerId, Guid[] Comptes);
public sealed record CompteAval(Guid Id, string Iban, string Libelle, string Type, string Devise, decimal Solde);
public sealed record OperationAval(DateTimeOffset Date, string LibelleBrut, string Categorie, decimal Montant);
public sealed record PageOperationsAval(int Page, int Taille, int Total, OperationAval[] Operations);
public sealed record ConseillerAval(string Nom, string Telephone, string Email, string Agence);
public sealed record OffreAval(string Titre, string Texte, string Image);
public sealed record SoldeAval(Guid Id, decimal Solde);

/// <summary>Un service en aval a répondu par une erreur : le BFF la traduit pour son écran (voir Program.cs).</summary>
public sealed class ServiceAvalException(int statut, string corps) : Exception($"Service en aval : {statut}")
{
    public int Statut => statut;
    public string Corps => corps;
}

/// <summary>Combien d'appels aux services une requête du navigateur a coûté, et combien de temps ils ont pris en tout.</summary>
public sealed class MesureAval
{
    private int _appels;
    private long _ticks;

    public int Appels => _appels;
    public TimeSpan DureeCumulee => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public void Noter(TimeSpan duree)
    {
        Interlocked.Increment(ref _appels);
        Interlocked.Add(ref _ticks, duree.Ticks);
    }
}

/// <summary>
/// Le client HTTP des services, propre à CE BFF : le BFF mobile a le sien. Chaque appel porte le jeton de l'utilisateur,
/// conservé côté serveur dans sa session : le navigateur ne le voit jamais.
/// </summary>
public sealed class ClientServices(HttpClient http, MesureAval mesure)
{
    public async Task<JetonAval> ObtenirJeton(string utilisateur, string motDePasse)
    {
        using var reponse = await Envoyer(new HttpRequestMessage(HttpMethod.Post, "/identite/jeton")
        {
            Content = JsonContent.Create(new { utilisateur, motDePasse })
        }, jeton: null);
        return (await reponse.Content.ReadFromJsonAsync<JetonAval>())!;
    }

    public Task<ProfilAval> Profil(string jeton) => Lire<ProfilAval>("/profils/moi", jeton);

    public Task<CompteAval> Compte(Guid id, string jeton) => Lire<CompteAval>($"/comptes/{id}", jeton);

    public Task<PageOperationsAval> Operations(Guid id, int taille, string jeton) =>
        Lire<PageOperationsAval>($"/comptes/{id}/operations?page=1&taille={taille}", jeton);

    public Task<ConseillerAval> Conseiller(string id, string jeton) => Lire<ConseillerAval>($"/conseillers/{id}", jeton);

    public Task<OffreAval[]> Offres(string segment, string jeton, CancellationToken annulation) =>
        Lire<OffreAval[]>($"/offres?segment={Uri.EscapeDataString(segment)}", jeton, annulation);

    public async Task<SoldeAval> Virer(Guid source, Guid cible, decimal montant, string jeton)
    {
        using var reponse = await Envoyer(new HttpRequestMessage(HttpMethod.Post, "/virements")
        {
            Content = JsonContent.Create(new { source, cible, montant })
        }, jeton);
        return (await reponse.Content.ReadFromJsonAsync<SoldeAval>())!;
    }

    private async Task<T> Lire<T>(string chemin, string jeton, CancellationToken annulation = default)
    {
        using var reponse = await Envoyer(new HttpRequestMessage(HttpMethod.Get, chemin), jeton, annulation);
        return (await reponse.Content.ReadFromJsonAsync<T>(annulation))!;
    }

    private async Task<HttpResponseMessage> Envoyer(HttpRequestMessage requete, string? jeton, CancellationToken annulation = default)
    {
        using (requete)
        {
            if (jeton is not null)
                requete.Headers.Authorization = new("Bearer", jeton);
            var debut = Stopwatch.GetTimestamp();
            var reponse = await http.SendAsync(requete, annulation);
            mesure.Noter(Stopwatch.GetElapsedTime(debut));
            if (reponse.IsSuccessStatusCode)
                return reponse;
            using (reponse)
                throw new ServiceAvalException((int)reponse.StatusCode, await reponse.Content.ReadAsStringAsync(annulation));
        }
    }
}
