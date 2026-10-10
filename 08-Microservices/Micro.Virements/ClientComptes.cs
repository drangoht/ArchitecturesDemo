using System.Net;
using System.Text.Json;

namespace Micro.Virements;

// Représentation LOCALE d'un compte : pas de bibliothèque partagée avec le service Comptes,
// le contrat entre les deux, c'est le JSON échangé.
internal record CompteDistant(Guid Id, string Titulaire, decimal Solde);

internal enum Issue { Accepte, Refuse, Introuvable }

internal record ReponseMouvement(Issue Issue, string? Erreur = null);

/// <summary>Client HTTP du service Comptes. Une panne réseau remonte en exception (après les retries).</summary>
internal class ClientComptes(IHttpClientFactory fabrique)
{
    private HttpClient Http => fabrique.CreateClient("comptes");

    public async Task<CompteDistant?> Consulter(Guid id)
    {
        var reponse = await Http.GetAsync($"/comptes/{id}");
        if (reponse.StatusCode == HttpStatusCode.NotFound)
            return null;
        reponse.EnsureSuccessStatusCode();
        return await reponse.Content.ReadFromJsonAsync<CompteDistant>();
    }

    public Task<ReponseMouvement> Debiter(Guid id, decimal montant, string operationId) =>
        Mouvement(id, "debits", montant, operationId);

    public Task<ReponseMouvement> Crediter(Guid id, decimal montant, string operationId) =>
        Mouvement(id, "credits", montant, operationId);

    private async Task<ReponseMouvement> Mouvement(Guid id, string type, decimal montant, string operationId)
    {
        var reponse = await Http.PostAsJsonAsync($"/interne/comptes/{id}/{type}", new { operationId, montant });
        return reponse.StatusCode switch
        {
            HttpStatusCode.OK => new ReponseMouvement(Issue.Accepte),
            HttpStatusCode.NotFound => new ReponseMouvement(Issue.Introuvable, $"Compte {id} introuvable."),
            HttpStatusCode.BadRequest => new ReponseMouvement(Issue.Refuse, await LireErreur(reponse)),
            _ => throw new HttpRequestException($"Le service Comptes a répondu {(int)reponse.StatusCode}.")
        };
    }

    private static async Task<string?> LireErreur(HttpResponseMessage reponse) =>
        (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("erreur").GetString();
}
