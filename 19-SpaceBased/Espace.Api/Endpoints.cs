using System.Diagnostics;
using Espace.Banque;
using Espace.Grille;

namespace Espace.Api;

internal static class Endpoints
{
    internal record OuvertureRequete(string Titulaire, decimal DepotInitial);
    internal record MontantRequete(decimal Montant);
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);
    internal record RoutageRequete(ModeRoutage Mode);
    internal record RetraitsConcurrentsRequete(Guid Compte, int Nombre, decimal Montant);
    internal record ChargeRequete(int Nombre);

    /// <summary>
    /// Chaque route transforme la requête HTTP en requête métier et la confie à la grille de messages, qui choisit l'unité.
    /// L'en-tête X-Unite dit quelle unité a répondu.
    /// </summary>
    public static void MapBanque(this IEndpointRouteBuilder app)
    {
        app.MapGet("/comptes/{id:guid}", (Guid id, Plateforme p, HttpContext h) => Envoyer(p, h, new ConsulterCompte(id)));

        app.MapPost("/comptes", async (OuvertureRequete r, Plateforme p, HttpContext h) =>
        {
            var id = Guid.NewGuid();
            var resultat = await Envoyer(p, h, new OuvrirCompte(id, r.Titulaire, r.DepotInitial));
            return resultat is IValueHttpResult { Value: CompteDto compte } ? Results.Created($"/comptes/{id}", compte) : resultat;
        });

        app.MapPost("/comptes/{id:guid}/depot", (Guid id, MontantRequete r, Plateforme p, HttpContext h) =>
            Envoyer(p, h, new Deposer(id, r.Montant)));

        app.MapPost("/comptes/{id:guid}/retrait", (Guid id, MontantRequete r, Plateforme p, HttpContext h) =>
            Envoyer(p, h, new Retirer(id, r.Montant)));

        app.MapPost("/virements", (VirementRequete r, Plateforme p, HttpContext h) =>
            Envoyer(p, h, new Virer(r.Source, r.Cible, r.Montant)));
    }

    /// <summary>Observer et manipuler l'intergiciel : unités, routage, réplication, pompe, base.</summary>
    public static void MapGrille(this IEndpointRouteBuilder app)
    {
        var grille = app.MapGroup("/grille");

        grille.MapGet("/", (Plateforme p) => Results.Ok(p.Etat()));

        grille.MapGet("/journal", (Plateforme p) => Results.Ok(p.Journal()));

        // Une donnée vue par chaque réplique et par la base : identiques au repos, différentes pendant la réplication.
        grille.MapGet("/comparaison/{id:guid}", (Guid id, Plateforme p) => Results.Ok(new
        {
            repliques = p.Donnees.Repliques().OrderBy(r => r.Unite).Select(r => Vue(r.Unite, r.Lire(id.ToString()))),
            @base = Vue("base", p.Base.Lire(id.ToString()))
        }));

        grille.MapPost("/routage", (RoutageRequete r, Plateforme p) =>
        {
            p.Messages.Mode = r.Mode;
            return Results.Ok(new { routage = p.Messages.Mode });
        });

        grille.MapPost("/unites", async (Plateforme p) => Results.Ok(new { unite = (await p.Deploiement.Demarrer()).Id }));

        grille.MapDelete("/unites/{id}", async (string id, Plateforme p) =>
        {
            if (Trouver(p, id) is not { } unite)
                return Results.NotFound();
            await p.Deploiement.Arreter(unite);
            return Results.NoContent();
        });

        grille.MapPost("/unites/{id}/panne", (string id, Plateforme p) =>
        {
            if (Trouver(p, id) is not { } unite)
                return Results.NotFound();
            p.Deploiement.Tuer(unite);
            return Results.NoContent();
        });

        // Toutes les unités tombent : la grille en mémoire disparaît. La requête suivante provoque un démarrage à froid.
        grille.MapPost("/panne-totale", (Plateforme p) =>
        {
            foreach (var unite in p.Messages.Unites())
                p.Deploiement.Tuer(unite);
            return Results.NoContent();
        });

        grille.MapPost("/base/panne", (Plateforme p) =>
        {
            p.Base.Disponible = false;
            return Results.NoContent();
        });

        grille.MapPost("/base/retablir", (Plateforme p) =>
        {
            p.Base.Disponible = true;
            return Results.NoContent();
        });

        grille.MapPost("/evaluer", async (Plateforme p) => Results.Ok(new { decision = await p.Deploiement.Evaluer() }));
    }

    /// <summary>Les démonstrations : collisions selon le routage, élasticité sous la charge.</summary>
    public static void MapDemonstrations(this IEndpointRouteBuilder app)
    {
        // Beaucoup de retraits simultanés sur UN compte. En affinité, une seule unité les traite : le compte est juste.
        // Au tourniquet, plusieurs unités écrivent le même compte en même temps : des écritures se perdent.
        app.MapPost("/demo/retraits-concurrents", async (RetraitsConcurrentsRequete r, Plateforme p) =>
        {
            if (r.Nombre is < 1 or > 5_000)
                return Results.BadRequest(new { erreur = "Nombre : de 1 à 5 000." });
            if ((await p.Envoyer(new ConsulterCompte(r.Compte))).Reponse is not CompteDto avant)
                return Results.NotFound();
            var perduesAvant = p.Donnees.MisesAJourPerdues;

            var resultats = await Task.WhenAll(Enumerable.Range(0, r.Nombre)
                .Select(_ => Task.Run(() => p.Envoyer(new Retirer(r.Compte, r.Montant)))));
            await p.AttendreStabilisation();

            var acceptes = resultats.Count(x => x.Reponse is CompteDto);
            var cle = r.Compte.ToString();
            return Results.Ok(new
            {
                routage = p.Messages.Mode,
                unitesSollicitees = resultats.Select(x => x.Unite).Distinct().Order(),
                soldeAvant = avant.Solde,
                acceptes,
                refuses = r.Nombre - acceptes,
                soldeAttendu = avant.Solde - acceptes * r.Montant,
                soldeDansLaGrille = p.Donnees.Repliques().Select(x => ((Compte)x.Lire(cle)!.Valeur).Solde).Distinct(),
                soldeDansLaBase = ((Compte)p.Base.Lire(cle)!.Valeur).Solde,
                misesAJourPerdues = p.Donnees.MisesAJourPerdues - perduesAvant
            });
        });

        // Un afflux d'ouvertures de comptes (des clés toutes différentes, donc réparties entre les unités).
        app.MapPost("/demo/charge", async (ChargeRequete r, Plateforme p) =>
        {
            if (r.Nombre is < 1 or > 5_000)
                return Results.BadRequest(new { erreur = "Nombre : de 1 à 5 000." });
            var unitesAvant = p.Messages.Unites().Count;
            var unitesMax = unitesAvant;
            using var fin = new CancellationTokenSource();
            var observation = Task.Run(async () =>
            {
                while (!fin.IsCancellationRequested)
                {
                    unitesMax = Math.Max(unitesMax, p.Messages.Unites().Count);
                    await Task.Delay(20);
                }
            });

            var chrono = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(1, r.Nombre)
                .Select(i => Task.Run(() => p.Envoyer(new OuvrirCompte(Guid.NewGuid(), $"Client {i}", 10m)))));
            chrono.Stop();
            await fin.CancelAsync();
            await observation;

            return Results.Ok(new
            {
                requetes = r.Nombre,
                dureeMs = chrono.ElapsedMilliseconds,
                unitesAvant,
                unitesMax,
                unitesMaintenant = p.Messages.Unites().Count,
                repartition = p.Messages.Unites().Select(u => new { u.Id, u.Traitees })
            });
        });
    }

    private static async Task<IResult> Envoyer(Plateforme plateforme, HttpContext http, IRequete requete)
    {
        var (reponse, unite) = await plateforme.Envoyer(requete);
        http.Response.Headers["X-Unite"] = unite;
        return reponse switch
        {
            Refus { Introuvable: true } r => Results.NotFound(new { erreur = r.Motif }),
            Refus r => Results.BadRequest(new { erreur = r.Motif }),
            _ => Results.Ok(reponse)
        };
    }

    private static UniteDeTraitement? Trouver(Plateforme p, string id) => p.Messages.Unites().FirstOrDefault(u => u.Id == id);

    private static object Vue(string ou, Entree? entree) =>
        new { ou, version = entree?.Version.ToString(), solde = (entree?.Valeur as Compte)?.Solde, operations = (entree?.Valeur as Compte)?.Operations.Count };
}
