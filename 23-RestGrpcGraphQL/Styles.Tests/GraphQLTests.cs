using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Styles.Outillage.GraphQL;

namespace Styles.Tests;

public class GraphQLTests : IAsyncLifetime
{
    private readonly Systeme _systeme = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _systeme.DisposeAsync();

    private static string[] Cles(JsonNode? objet) => objet!.AsObject().Select(p => p.Key).ToArray();

    private static string Code(JsonObject reponse, int index = 0) => reponse["errors"]![index]!["extensions"]!["code"]!.GetValue<string>();

    [Fact]
    public void Le_client_obtient_exactement_les_champs_demandes_et_rien_d_autre()
    {
        var reponse = _systeme.Requete("""{ compte(numero: "10001") { libelle solde } }""");

        Assert.Equal(["libelle", "solde"], Cles(reponse["data"]!["compte"]));
        Assert.Null(reponse["errors"]);
    }

    [Fact]
    public void Des_alias_permettent_de_demander_deux_fois_le_meme_champ()
    {
        var reponse = _systeme.Requete("""{ courant: compte(numero: "10001") { solde } livret: compte(numero: "10002") { solde } __typename }""");

        Assert.Equal(["courant", "livret", "__typename"], Cles(reponse["data"]));
        Assert.Equal(8150.0, reponse["data"]!["livret"]!["solde"]!.GetValue<double>());
        Assert.Equal("Query", reponse["data"]!["__typename"]!.GetValue<string>());
    }

    [Fact]
    public void Les_operations_de_tous_les_comptes_d_un_niveau_sont_lues_en_une_seule_fois()
    {
        var avant = _systeme.Banque.Statistiques.LecturesOperations;
        var reponse = _systeme.Requete("""{ client(id: "alice") { comptes { libelle operations(derniers: 2) { libelle } } } }""");

        Assert.Equal(3, reponse["data"]!["client"]!["comptes"]!.AsArray().Count);
        Assert.Equal(1, _systeme.Banque.Statistiques.LecturesOperations - avant);   // et non 3 : le résolveur est groupé

        // Même à un niveau plus profond, sur 9 comptes (3 comptes × leurs titulaires × 3 comptes), une seule lecture.
        avant = _systeme.Banque.Statistiques.LecturesOperations;
        _systeme.Requete("""{ client(id: "alice") { comptes { titulaire { comptes { operations { id } } } } } }""");
        Assert.Equal(1, _systeme.Banque.Statistiques.LecturesOperations - avant);
    }

    [Fact]
    public void Une_requete_trop_profonde_est_refusee_avant_d_etre_executee()
    {
        var avant = _systeme.Banque.Statistiques.LecturesOperations;
        var reponse = _systeme.Requete("""{ client(id: "alice") { comptes { titulaire { comptes { titulaire { comptes { operations { id } } } } } } } }""");

        Assert.Null(reponse["data"]);
        Assert.Equal("GRAPHQL_VALIDATION_FAILED", Code(reponse));
        Assert.Contains("profondeur maximale de 6", reponse["errors"]![0]!["message"]!.GetValue<string>());
        Assert.Equal(avant, _systeme.Banque.Statistiques.LecturesOperations);   // rien n'a été exécuté
    }

    [Fact]
    public void Un_champ_ou_un_argument_inconnu_est_refuse_par_la_validation_avec_sa_position()
    {
        var reponse = _systeme.Requete("""
            {
              compte(numero: "10001") { libelle soldes }
              client(identifiant: "alice") { nom }
            }
            """);

        Assert.False(reponse.ContainsKey("data"));   // une requête invalide n'est pas exécutée du tout
        var erreurs = reponse["errors"]!.AsArray();
        Assert.Equal(3, erreurs.Count);   // le champ inconnu, l'argument inconnu, l'argument obligatoire manquant
        Assert.Equal("Le champ « soldes » n'existe pas sur le type « Compte ».", erreurs[0]!["message"]!.GetValue<string>());
        Assert.Equal((2, 37), (erreurs[0]!["locations"]![0]!["line"]!.GetValue<int>(), erreurs[0]!["locations"]![0]!["column"]!.GetValue<int>()));
        Assert.Contains("« id » est obligatoire", erreurs[2]!["message"]!.GetValue<string>());
    }

    [Fact]
    public void Une_erreur_de_syntaxe_indique_la_ligne_et_la_colonne()
    {
        var reponse = _systeme.Requete("{\n  compte(numero: \"10001\" { libelle }\n}");

        Assert.Equal("GRAPHQL_PARSE_FAILED", Code(reponse));
        Assert.Equal(2, reponse["errors"]![0]!["locations"]![0]!["line"]!.GetValue<int>());
    }

    [Fact]
    public void Une_erreur_sur_un_champ_n_empeche_pas_d_obtenir_les_autres()
    {
        var reponse = _systeme.Requete("""{ a: compte(numero: "10001") { libelle } b: compte(numero: "99999") { libelle } }""");

        Assert.Equal("Compte courant", reponse["data"]!["a"]!["libelle"]!.GetValue<string>());
        Assert.Null(reponse["data"]!["b"]);
        var erreur = Assert.Single(reponse["errors"]!.AsArray())!;
        Assert.Equal("COMPTE_INCONNU", erreur["extensions"]!["code"]!.GetValue<string>());
        Assert.Equal("[\"b\"]", erreur["path"]!.ToJsonString());
    }

    [Fact]
    public void Un_champ_non_nul_en_erreur_rend_nul_le_premier_parent_nullable()
    {
        // operations est « [Operation!]! » et échoue (51 > 50) : le compte ne peut pas valoir null (« [Compte!]! »),
        // la liste non plus… la nullité remonte jusqu'à « client », le premier champ qui l'accepte.
        var reponse = _systeme.Requete("""{ client(id: "alice") { nom comptes { operations(derniers: 51) { id } } } }""");

        Assert.Null(reponse["data"]!["client"]);
        var erreurs = reponse["errors"]!.AsArray();
        Assert.Equal(3, erreurs.Count);   // une par compte
        Assert.Equal("[\"client\",\"comptes\",0,\"operations\"]", erreurs[0]!["path"]!.ToJsonString());
        Assert.All(erreurs, e => Assert.Equal("PARAMETRE_INVALIDE", e!["extensions"]!["code"]!.GetValue<string>()));
    }

    [Fact]
    public void Les_variables_sont_verifiees_selon_leur_type()
    {
        const string requete = "query ($numero: ID!, $n: Int) { compte(numero: $numero) { operations(derniers: $n) { id } } }";

        var correcte = _systeme.Requete(requete, new { numero = "10001", n = 2 });
        Assert.Equal(2, correcte["data"]!["compte"]!["operations"]!.AsArray().Count);

        var sansN = _systeme.Requete(requete, new { numero = "10001" });   // $n absente : l'argument garde son défaut (10)
        Assert.Equal(6, sansN["data"]!["compte"]!["operations"]!.AsArray().Count);

        Assert.Equal("BAD_USER_INPUT", Code(_systeme.Requete(requete, new { numero = "10001", n = "trois" })));
        Assert.Equal("BAD_USER_INPUT", Code(_systeme.Requete(requete, new { n = 2 })));
        Assert.Equal("GRAPHQL_VALIDATION_FAILED", Code(_systeme.Requete("query ($x: Int) { compte(numero: \"10001\") { libelle } }")));
        Assert.Equal("GRAPHQL_VALIDATION_FAILED", Code(_systeme.Requete("query ($n: String) { compte(numero: \"10001\") { operations(derniers: $n) { id } } }")));
    }

    [Fact]
    public void Une_mutation_s_execute_et_rend_ce_que_le_client_demande()
    {
        var reponse = _systeme.Requete("""
            mutation { effectuerVirement(source: "10001", cible: "20001", montant: 25.5, libelle: "Cinéma") { id montant source { solde } cible { solde } } }
            """);

        var virement = reponse["data"]!["effectuerVirement"]!;
        Assert.Equal("V001", virement["id"]!.GetValue<string>());
        Assert.Equal(1600.55, virement["source"]!["solde"]!.GetValue<double>(), 2);
        Assert.Equal(1185.51, virement["cible"]!["solde"]!.GetValue<double>(), 2);
    }

    [Fact]
    public async Task Le_schema_publie_signale_les_champs_deprecies_et_le_serveur_sait_qui_les_utilise()
    {
        var sdl = await _systeme.Http.GetStringAsync("/graphql/schema");
        Assert.Contains("soldeTexte: String! @deprecated(reason: \"Utiliser « solde » (un nombre) et le formater à l'affichage.\")", sdl);
        Assert.Contains("operations(derniers: Int = 10): [Operation!]!", sdl);

        Assert.False(_systeme.Moteur.Utilisation.ContainsKey("Compte.soldeTexte"));
        var reponse = _systeme.Requete("""{ compte(numero: "10002") { soldeTexte } }""");
        Assert.Equal("8 150,00 €", reponse["data"]!["compte"]!["soldeTexte"]!.GetValue<string>().Replace(' ', ' ').Replace(' ', ' '));
        Assert.Equal(1, _systeme.Moteur.Utilisation["Compte.soldeTexte"]);
    }

    [Fact]
    public void Une_erreur_inattendue_ne_divulgue_rien_au_client()
    {
        var schema = new Schema();
        schema.Query.Champ("fragile", "String", _ => throw new InvalidOperationException("mot de passe de la base : secret"));
        var reponse = new MoteurGraphQL(schema).Executer("{ fragile }");

        Assert.Null(reponse["data"]!["fragile"]);
        Assert.Equal("Erreur interne.", reponse["errors"]![0]!["message"]!.GetValue<string>());
        Assert.DoesNotContain("secret", reponse.ToJsonString());
    }

    [Fact]
    public async Task Par_HTTP_tout_passe_par_POST_graphql_et_repond_200_meme_en_cas_d_erreur()
    {
        var reponse = await _systeme.Http.PostAsJsonAsync("/graphql", new { query = """{ compte(numero: "99999") { libelle } }""" });

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var corps = (await reponse.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("COMPTE_INCONNU", Code(corps));
    }
}
