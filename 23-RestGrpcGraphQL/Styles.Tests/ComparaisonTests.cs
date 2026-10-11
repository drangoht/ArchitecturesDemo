using Styles.Client;

namespace Styles.Tests;

/// <summary>Les mêmes usages, par les trois API : même résultat, coûts différents.</summary>
public class ComparaisonTests : IAsyncLifetime
{
    private readonly Systeme _systeme = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _systeme.DisposeAsync();

    private async Task<(EcranAccueil Ecran, Mesure Mesure, long Lectures)> Ecran(IClientBanque client, CompteurHttp compteur, string clientId)
    {
        var avant = _systeme.Banque.Statistiques.LecturesOperations;
        var (ecran, mesure) = await Chrono.Mesurer(compteur, () => client.EcranAccueil(clientId));
        return (ecran, mesure, _systeme.Banque.Statistiques.LecturesOperations - avant);
    }

    [Fact]
    public async Task Les_trois_API_donnent_exactement_le_meme_ecran()
    {
        // Une opération datée « maintenant » (avec des millisecondes) : chaque API a sa précision pour les dates.
        await _systeme.Rest.Virer("20001", "10001", 19.99m, "Remboursement");

        var rest = await _systeme.Rest.EcranAccueil("alice");
        var graphql = await _systeme.GraphQL.EcranAccueil("alice");
        var grpc = await _systeme.Grpc.EcranAccueil("alice");

        foreach (var autre in new[] { graphql, grpc })
        {
            Assert.Equal(rest.Nom, autre.Nom);
            Assert.Equal(rest.Comptes.Select(c => (c.Numero, c.Libelle, c.Solde)), autre.Comptes.Select(c => (c.Numero, c.Libelle, c.Solde)));
            Assert.Equal(rest.Comptes.SelectMany(c => c.Operations), autre.Comptes.SelectMany(c => c.Operations));
        }
        Assert.Equal(1646.04m, rest.Comptes[0].Solde);
        Assert.Equal("Remboursement de 20001", rest.Comptes[0].Operations[0].Libelle);
    }

    [Fact]
    public async Task REST_fait_une_requete_par_compte_GraphQL_et_gRPC_une_seule_en_tout()
    {
        Assert.Equal(5, (await Ecran(_systeme.Rest, _systeme.CompteurRest, "alice")).Mesure.Requetes);   // client + comptes + 3 × opérations
        Assert.Equal(1, (await Ecran(_systeme.GraphQL, _systeme.CompteurGraphQL, "alice")).Mesure.Requetes);
        Assert.Equal(1, (await Ecran(_systeme.Grpc, _systeme.CompteurGrpc, "alice")).Mesure.Requetes);

        Assert.Equal(3, (await Ecran(_systeme.Rest, _systeme.CompteurRest, "bob")).Mesure.Requetes);     // un seul compte
    }

    [Fact]
    public async Task Derriere_la_requete_unique_de_GraphQL_la_base_n_est_lue_qu_une_fois()
    {
        Assert.Equal(3, (await Ecran(_systeme.Rest, _systeme.CompteurRest, "alice")).Lectures);
        Assert.Equal(1, (await Ecran(_systeme.GraphQL, _systeme.CompteurGraphQL, "alice")).Lectures);
        Assert.Equal(1, (await Ecran(_systeme.Grpc, _systeme.CompteurGrpc, "alice")).Lectures);
    }

    [Fact]
    public async Task gRPC_transfere_le_moins_d_octets_et_REST_le_plus()
    {
        var rest = (await Ecran(_systeme.Rest, _systeme.CompteurRest, "alice")).Mesure;
        var graphql = (await Ecran(_systeme.GraphQL, _systeme.CompteurGraphQL, "alice")).Mesure;
        var grpc = (await Ecran(_systeme.Grpc, _systeme.CompteurGrpc, "alice")).Mesure;

        // Reçus : REST envoie des représentations complètes, GraphQL seulement les champs demandés (en JSON, noms compris),
        // gRPC des numéros de champ et des entiers binaires.
        Assert.True(grpc.OctetsRecus < graphql.OctetsRecus, $"gRPC {grpc.OctetsRecus} o, GraphQL {graphql.OctetsRecus} o");
        Assert.True(graphql.OctetsRecus * 2 < rest.OctetsRecus, $"GraphQL {graphql.OctetsRecus} o, REST {rest.OctetsRecus} o");
        // Envoyés : REST n'envoie rien (des GET), GraphQL envoie le texte de sa requête, gRPC un petit message binaire.
        Assert.Equal(0, rest.OctetsEnvoyes);
        Assert.True(grpc.OctetsEnvoyes < 20 && graphql.OctetsEnvoyes > 150, $"gRPC {grpc.OctetsEnvoyes} o, GraphQL {graphql.OctetsEnvoyes} o");
    }

    [Theory]
    [InlineData("20001", "10001", 1_000_000, "422 SOLDE_INSUFFISANT", "SOLDE_INSUFFISANT", "FailedPrecondition")]
    [InlineData("20001", "99999", 10, "404 COMPTE_INCONNU", "COMPTE_INCONNU", "NotFound")]
    [InlineData("20001", "10001", -5, "400 MONTANT_INVALIDE", "MONTANT_INVALIDE", "InvalidArgument")]
    public async Task La_meme_erreur_metier_dans_la_langue_de_chaque_API(string source, string cible, decimal montant,
        string rest, string graphql, string grpc)
    {
        var resultats = new List<ResultatVirement>();
        foreach (var client in _systeme.LesTrois)
            resultats.Add(await client.Virer(source, cible, montant, "Essai"));

        Assert.Equal([rest, graphql, grpc], resultats.Select(r => r.Erreur));
        Assert.Single(resultats.Select(r => r.Message).Distinct());   // le message lisible, lui, est le même partout
    }

    [Fact]
    public async Task Un_virement_reussit_de_la_meme_facon_par_les_trois_API()
    {
        var soldes = new List<decimal?>();
        foreach (var client in _systeme.LesTrois)
            soldes.Add((await client.Virer("10001", "20001", 100m, $"Par {client.Nom}")).SoldeSource);

        Assert.Equal([1526.05m, 1426.05m, 1326.05m], soldes);
        Assert.Equal(1460.01m, _systeme.Banque.Compte("20001").Solde);
    }
}
