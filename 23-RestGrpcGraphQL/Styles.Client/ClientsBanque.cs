using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Styles.Outillage.Grpc;
using P = Styles.Contrats;

namespace Styles.Client;

// Ce qu'affiche l'écran d'accueil de l'application mobile : le nom du client, et pour chaque compte son libellé, son
// solde et ses trois dernières opérations. Rien de plus.
public sealed record LigneOperation(DateTimeOffset Date, string Libelle, decimal Montant);
public sealed record LigneCompte(string Numero, string Libelle, decimal Solde, IReadOnlyList<LigneOperation> Operations);
public sealed record EcranAccueil(string Nom, IReadOnlyList<LigneCompte> Comptes);

/// <summary>Le résultat d'un virement ; en cas d'échec, l'erreur telle que la donne chaque protocole.</summary>
public sealed record ResultatVirement(string? Id, decimal? SoldeSource, string? Erreur, string? Message);

/// <summary>La même application, trois façons de parler au serveur.</summary>
public interface IClientBanque
{
    string Nom { get; }
    Task<EcranAccueil> EcranAccueil(string clientId, int operationsParCompte = 3);
    Task<ResultatVirement> Virer(string source, string cible, decimal montant, string libelle);
}

/// <summary>REST : on suit les ressources. Le client, puis ses comptes, puis les opérations de CHAQUE compte.</summary>
public sealed class ClientRest(HttpClient http) : IClientBanque
{
    public string Nom => "REST";

    public async Task<EcranAccueil> EcranAccueil(string clientId, int operationsParCompte = 3)
    {
        var client = await http.GetFromJsonAsync<JsonObject>($"/clients/{clientId}");
        var comptes = await http.GetFromJsonAsync<JsonArray>($"/clients/{clientId}/comptes");
        // 1 + 1 + N : une requête par compte pour ses opérations (en parallèle, mais une requête chacune).
        var lignes = await Task.WhenAll(comptes!.Select(async c =>
        {
            var numero = c!["numero"]!.GetValue<string>();
            var operations = await http.GetFromJsonAsync<JsonArray>($"/comptes/{numero}/operations?limite={operationsParCompte}");
            return new LigneCompte(numero, c["libelle"]!.GetValue<string>(), c["solde"]!.GetValue<decimal>(),
                operations!.Select(o => new LigneOperation(o!["date"]!.GetValue<DateTimeOffset>(), o["libelle"]!.GetValue<string>(),
                    o["montant"]!.GetValue<decimal>())).ToList());
        }));
        return new EcranAccueil(client!["nom"]!.GetValue<string>(), lignes);
    }

    public async Task<ResultatVirement> Virer(string source, string cible, decimal montant, string libelle)
    {
        var reponse = await http.PostAsJsonAsync("/virements", new { source, cible, montant, libelle });
        var corps = await reponse.Content.ReadFromJsonAsync<JsonObject>();
        if (reponse.StatusCode == HttpStatusCode.Created)
            return new ResultatVirement(corps!["id"]!.GetValue<string>(), corps["soldeSource"]!.GetValue<decimal>(), null, null);
        // Le statut HTTP dit la catégorie, le corps « problem+json » le détail.
        return new ResultatVirement(null, null, $"{(int)reponse.StatusCode} {corps!["code"]!.GetValue<string>()}", corps["title"]?.GetValue<string>());
    }
}

/// <summary>GraphQL : une requête qui décrit tout l'écran, et la réponse a exactement sa forme.</summary>
public sealed class ClientGraphQL(HttpClient http) : IClientBanque
{
    public const string RequeteEcranAccueil = """
        query EcranAccueil($id: ID!, $n: Int) {
          client(id: $id) {
            nom
            comptes { numero libelle solde operations(derniers: $n) { date libelle montant } }
          }
        }
        """;

    public const string MutationVirement = """
        mutation Virer($source: ID!, $cible: ID!, $montant: Float!, $libelle: String) {
          effectuerVirement(source: $source, cible: $cible, montant: $montant, libelle: $libelle) {
            id
            source { solde }
          }
        }
        """;

    public string Nom => "GraphQL";

    public async Task<JsonObject> Executer(string requete, object? variables = null)
    {
        var reponse = await http.PostAsJsonAsync("/graphql", new { query = requete, variables });
        reponse.EnsureSuccessStatusCode();   // 200 même en cas d'erreur métier : c'est le corps qui compte
        return (await reponse.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    public async Task<EcranAccueil> EcranAccueil(string clientId, int operationsParCompte = 3)
    {
        var reponse = await Executer(RequeteEcranAccueil, new { id = clientId, n = operationsParCompte });
        var client = reponse["data"]?["client"] ?? throw new InvalidOperationException(reponse["errors"]?.ToJsonString());
        return new EcranAccueil(client["nom"]!.GetValue<string>(), client["comptes"]!.AsArray().Select(c => new LigneCompte(
            c!["numero"]!.GetValue<string>(), c["libelle"]!.GetValue<string>(), (decimal)c["solde"]!.GetValue<double>(),
            c["operations"]!.AsArray().Select(o => new LigneOperation(DateTimeOffset.Parse(o!["date"]!.GetValue<string>()),
                o["libelle"]!.GetValue<string>(), (decimal)o["montant"]!.GetValue<double>())).ToList())).ToList());
    }

    public async Task<ResultatVirement> Virer(string source, string cible, decimal montant, string libelle)
    {
        var reponse = await Executer(MutationVirement, new { source, cible, montant, libelle });
        if (reponse["errors"] is JsonArray erreurs)
            return new ResultatVirement(null, null, erreurs[0]!["extensions"]!["code"]!.GetValue<string>(), erreurs[0]!["message"]!.GetValue<string>());
        var virement = reponse["data"]!["effectuerVirement"]!;
        return new ResultatVirement(virement["id"]!.GetValue<string>(), (decimal)virement["source"]!["solde"]!.GetValue<double>(), null, null);
    }
}

/// <summary>gRPC : une procédure taillée pour l'écran, des messages binaires, un client typé.</summary>
public sealed class ClientGrpcBanque(HttpClient http) : IClientBanque
{
    private readonly ClientGrpc _grpc = new(http, P.ServiceBanque.Nom);

    public string Nom => "gRPC";

    public ClientGrpc Grpc => _grpc;

    public async Task<EcranAccueil> EcranAccueil(string clientId, int operationsParCompte = 3)
    {
        var tableau = await _grpc.Unaire<P.DemandeTableauDeBord, P.TableauDeBord>("ObtenirTableauDeBord",
            new P.DemandeTableauDeBord { ClientId = clientId, OperationsParCompte = operationsParCompte });
        return new EcranAccueil(tableau.Nom, tableau.Comptes.Select(c => new LigneCompte(c.Numero, c.Libelle, c.SoldeCentimes / 100m,
            c.DernieresOperations.Select(o => new LigneOperation(DateTimeOffset.FromUnixTimeMilliseconds(o.DateUnixMs), o.Libelle,
                o.MontantCentimes / 100m)).ToList())).ToList());
    }

    public async Task<ResultatVirement> Virer(string source, string cible, decimal montant, string libelle)
    {
        try
        {
            var virement = await _grpc.Unaire<P.DemandeVirement, P.Virement>("EffectuerVirement", new P.DemandeVirement
            {
                Source = source, Cible = cible, MontantCentimes = (long)(montant * 100), Libelle = libelle
            });
            return new ResultatVirement(virement.Id, virement.SoldeSourceCentimes / 100m, null, null);
        }
        catch (ErreurGrpc e)   // l'un des 17 statuts gRPC : la catégorie, pas le code métier
        {
            return new ResultatVirement(null, null, e.Statut.ToString(), e.Message);
        }
    }

    public IAsyncEnumerable<P.Operation> SuivreOperations(string numero, TimeSpan? delai = null, CancellationToken annulation = default) =>
        _grpc.Flux<P.DemandeSuivi, P.Operation>("SuivreOperations", new P.DemandeSuivi { Numero = numero }, delai, annulation);
}
