using Styles.Banque;

namespace Styles.Serveur;

/// <summary>
/// L'API REST : des RESSOURCES (clients, comptes, opérations, virements), chacune à son URL, manipulées avec les verbes
/// HTTP. Chaque ressource a UNE représentation, complète, la même pour tous les clients : l'écran qui ne veut que le
/// solde reçoit aussi l'IBAN, le titulaire, les liens… (sur-récupération), et l'écran qui veut les comptes ET leurs
/// opérations doit faire plusieurs appels (sous-récupération). En échange : HTTP fait le reste (cache, codes, outils).
/// </summary>
public static class ApiRest
{
    public static void MapRest(this IEndpointRouteBuilder app)
    {
        var rest = app.MapGroup("").AddEndpointFilter(async (contexte, suite) =>
        {
            try
            {
                return await suite(contexte);
            }
            catch (ErreurMetier e)   // une erreur métier devient un statut HTTP et un corps « application/problem+json »
            {
                return Results.Problem(statusCode: Traductions.StatutHttp(e.Code), title: e.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = Traductions.CodeGraphQL(e.Code) });
            }
        });

        rest.MapGet("/clients/{id}", (string id, ServiceBancaire banque) =>
        {
            var client = banque.Client(id);
            return Results.Ok(new
            {
                id = client.Id,
                nom = client.Nom,
                email = client.Email,
                _links = new { self = $"/clients/{id}", comptes = $"/clients/{id}/comptes" }
            });
        });

        rest.MapGet("/clients/{id}/comptes", (string id, ServiceBancaire banque) =>
            Results.Ok(banque.ComptesDe(id).Select(c => Representation(c, banque))));

        // GET est sûr et idempotent : il peut être mis en cache et revalidé. L'ETag change à chaque opération du compte ;
        // un client qui interroge souvent renvoie l'ETag qu'il a, et reçoit « 304 Not Modified » (sans corps) si rien n'a bougé.
        rest.MapGet("/comptes/{numero}", (string numero, ServiceBancaire banque, HttpContext http) =>
        {
            var compte = banque.Compte(numero);
            var etag = $"\"{compte.Numero}-v{compte.Version}\"";
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = "private, no-cache";   // gardez-le, mais revalidez avant usage
            return http.Request.Headers.IfNoneMatch == etag
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(Representation(compte, banque));
        });

        rest.MapGet("/comptes/{numero}/operations", (string numero, int? limite, ServiceBancaire banque) =>
            Results.Ok(banque.DernieresOperations(numero, limite ?? 10).Select(o => new
            {
                id = o.Id,
                date = o.Date,
                libelle = o.Libelle,
                montant = o.Montant,
                devise = "EUR"
            })));

        // POST n'est pas idempotent : il crée une nouvelle ressource, dont l'adresse est rendue dans « Location ».
        rest.MapPost("/virements", (DemandeVirementRest demande, ServiceBancaire banque) =>
        {
            var virement = banque.EffectuerVirement(demande.Source, demande.Cible, demande.Montant, demande.Libelle);
            return Results.Created($"/virements/{virement.Id}", Representation(virement));
        });

        rest.MapGet("/virements/{id}", (string id, ServiceBancaire banque) => Results.Ok(Representation(banque.Virement(id))));
    }

    public sealed record DemandeVirementRest(string Source, string Cible, decimal Montant, string? Libelle);

    private static object Representation(Compte compte, ServiceBancaire banque)
    {
        var titulaire = banque.Client(compte.ClientId);
        return new
        {
            numero = compte.Numero,
            libelle = compte.Libelle,
            type = compte.Type.ToString().ToLowerInvariant(),
            iban = compte.Iban,
            solde = compte.Solde,
            devise = "EUR",
            dateOuverture = compte.DateOuverture,
            plafondDecouvert = compte.PlafondDecouvert,
            titulaire = new { id = titulaire.Id, nom = titulaire.Nom },
            _links = new
            {
                self = $"/comptes/{compte.Numero}",
                operations = $"/comptes/{compte.Numero}/operations",
                titulaire = $"/clients/{titulaire.Id}"
            }
        };
    }

    private static object Representation(Virement virement) => new
    {
        id = virement.Id,
        source = virement.Source,
        cible = virement.Cible,
        montant = virement.Montant,
        libelle = virement.Libelle,
        date = virement.Date,
        soldeSource = virement.SoldeSource,
        _links = new
        {
            self = $"/virements/{virement.Id}",
            source = $"/comptes/{virement.Source}",
            cible = $"/comptes/{virement.Cible}"
        }
    };
}
