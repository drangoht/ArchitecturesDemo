using System.Diagnostics;
using System.Net.Http.Json;

namespace Bff.Mobile;

/// <summary>Repère de l'assemblage, pour les tests.</summary>
public sealed class Marqueur;

// Copie volontaire, et non projet partagé avec le BFF web : chaque BFF appartient à l'équipe de SON application
// et évolue à son rythme. Seuls les champs utiles à l'application mobile sont lus.
public sealed record Connexion(string Utilisateur, string MotDePasse);
public sealed record VirementRequete(Guid Source, Guid Cible, decimal Montant);

public sealed record JetonAval(string Jeton, int ExpireDans);
public sealed record ProfilAval(string Prenom, string Segment, Guid[] Comptes);
public sealed record CompteAval(Guid Id, string Libelle, decimal Solde);
public sealed record OperationAval(DateTimeOffset Date, string LibelleBrut, string Categorie, decimal Montant);
public sealed record PageOperationsAval(int Page, int Taille, int Total, OperationAval[] Operations);
public sealed record OffreAval(string Titre);
public sealed record SoldeAval(Guid Id, decimal Solde);

public sealed class ServiceAvalException(int statut, string corps) : Exception($"Service en aval : {statut}")
{
    public int Statut => statut;
    public string Corps => corps;
}

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

/// <summary>Le jeton vient de l'application mobile elle-même (stockage sécurisé du téléphone) : le BFF le transmet.</summary>
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

    public Task<PageOperationsAval> Operations(Guid id, int page, int taille, string jeton) =>
        Lire<PageOperationsAval>($"/comptes/{id}/operations?page={page}&taille={taille}", jeton);

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
