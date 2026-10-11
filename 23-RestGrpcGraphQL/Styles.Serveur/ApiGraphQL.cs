using System.Globalization;
using System.Text.Json.Nodes;
using Styles.Banque;
using Styles.Outillage.GraphQL;

namespace Styles.Serveur;

/// <summary>
/// L'API GraphQL : un GRAPHE de types (Client → comptes → opérations…) et une seule adresse, /graphql. C'est le CLIENT
/// qui écrit sa requête : il demande exactement les champs qu'il affichera, sur plusieurs niveaux, en un aller-retour.
/// Le prix : le serveur doit se protéger des requêtes coûteuses (profondeur, N+1 en base) et le cache HTTP ne sert plus.
/// </summary>
public static class ApiGraphQL
{
    public static Schema Schema(ServiceBancaire banque)
    {
        var schema = new Schema();
        schema.Enum("TypeCompte", "COURANT", "EPARGNE");

        schema.Query
            .Champ("client", "Client", (_, a) => banque.Client(a.Lire<string>("id")), new Arg("id", "ID!"))
            .Champ("compte", "Compte", (_, a) => banque.Compte(a.Lire<string>("numero")), new Arg("numero", "ID!"));

        schema.DeclarerMutation()
            .Champ("effectuerVirement", "Virement!",
                (_, a) => banque.EffectuerVirement(a.Lire<string>("source"), a.Lire<string>("cible"),
                    (decimal)a.Lire<double>("montant"), a.LireOuDefaut<string>("libelle")),
                new Arg("source", "ID!"), new Arg("cible", "ID!"), new Arg("montant", "Float!"), new Arg("libelle", "String"));

        schema.Objet<Client>("Client")
            .Champ("id", "ID!", c => c.Id)
            .Champ("nom", "String!", c => c.Nom)
            .Champ("email", "String!", c => c.Email)
            .Champ("comptes", "[Compte!]!", c => banque.ComptesDe(c.Id));

        schema.Objet<Compte>("Compte")
            .Champ("numero", "ID!", c => c.Numero)
            .Champ("libelle", "String!", c => c.Libelle)
            .Champ("type", "TypeCompte!", c => c.Type)
            .Champ("iban", "String!", c => c.Iban)
            .Champ("solde", "Float!", c => c.Solde)
            // La première version de l'API rendait le solde déjà formaté. Le champ reste servi pour les anciens clients ;
            // la statistique d'utilisation (/graphql/utilisation) dira quand plus personne ne le demande.
            .Champ("soldeTexte", "String!", c => c.Solde.ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " €")
            .Deprecie("Utiliser « solde » (un nombre) et le formater à l'affichage.")
            // Résolveur GROUPÉ : pour tous les comptes d'un niveau, UNE lecture en base, au lieu d'une par compte.
            .ChampGroupe("operations", "[Operation!]!", (comptes, a) =>
            {
                var parCompte = banque.DernieresOperations(comptes.Select(c => c.Numero).ToList(), a.LireOuDefaut<int?>("derniers") ?? 10);
                return comptes.Select(c => (object?)parCompte[c.Numero]).ToList();
            }, new Arg("derniers", "Int", 10))
            .Champ("titulaire", "Client!", c => banque.Client(c.ClientId));

        schema.Objet<Operation>("Operation")
            .Champ("id", "ID!", o => o.Id)
            .Champ("date", "String!", o => o.Date.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture))
            .Champ("libelle", "String!", o => o.Libelle)
            .Champ("montant", "Float!", o => o.Montant);

        schema.Objet<Virement>("Virement")
            .Champ("id", "ID!", v => v.Id)
            .Champ("montant", "Float!", v => v.Montant)
            .Champ("libelle", "String!", v => v.Libelle)
            .Champ("source", "Compte!", v => banque.Compte(v.Source))
            .Champ("cible", "Compte!", v => banque.Compte(v.Cible));

        return schema;
    }

    public static MoteurGraphQL Moteur(ServiceBancaire banque) => new(Schema(banque), new OptionsGraphQL
    {
        ProfondeurMax = 6,
        TraduireErreur = e => e is ErreurMetier m ? new ErreurGraphQL(m.Message, Traductions.CodeGraphQL(m.Code)) : null
    });

    public sealed record RequeteGraphQL(string? Query, JsonObject? Variables, string? OperationName);

    public static void MapGraphQL(this IEndpointRouteBuilder app)
    {
        // Une seule adresse, toujours POST, toujours 200 dès que la requête a été comprise : le résultat (et les erreurs)
        // est dans le corps. Les outils HTTP (caches, journaux d'accès, alertes sur les 5xx) n'y voient plus rien.
        app.MapPost("/graphql", (RequeteGraphQL requete, MoteurGraphQL moteur) =>
            Results.Json(moteur.Executer(requete.Query, requete.Variables, requete.OperationName)));

        app.MapGet("/graphql/schema", (MoteurGraphQL moteur) => Results.Text(moteur.Schema.EnSdl(), "text/plain; charset=utf-8"));

        app.MapGet("/graphql/utilisation", (MoteurGraphQL moteur) => Results.Ok(moteur.Utilisation));
    }
}
