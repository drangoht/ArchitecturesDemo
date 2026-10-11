namespace Bff.Services;

/// <summary>Pannes et lenteurs simulées, réglables par PUT /chaos.</summary>
public sealed class Chaos
{
    public int LatenceMs { get; set; }
    public bool OffresEnPanne { get; set; }
}

internal static class Endpoints
{
    internal record DemandeJeton(string Utilisateur, string MotDePasse);
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);
    internal record ChaosRequete(int? LatenceMs, bool? OffresEnPanne);

    private static string Utilisateur(HttpContext http) => (string)http.Items["utilisateur"]!;

    private static IResult Interdit() =>
        Results.Json(new { erreur = "Cette donnée n'appartient pas à l'utilisateur du jeton." }, statusCode: StatusCodes.Status403Forbidden);

    public static void MapServices(this IEndpointRouteBuilder app)
    {
        // ─── Identité ───
        app.MapPost("/identite/jeton", (DemandeJeton d, Banque banque, Jetons jetons) =>
            banque.Utilisateurs.TryGetValue(d.Utilisateur, out var u) && u.MotDePasse == d.MotDePasse
                ? Results.Ok(new { jeton = jetons.Emettre(u.Id), expireDans = (int)Jetons.Duree.TotalSeconds })
                : Results.Json(new { erreur = "Identifiants incorrects." }, statusCode: StatusCodes.Status401Unauthorized));

        // ─── Profil : tout ce que la banque sait du client, quel que soit l'écran ───
        app.MapGet("/profils/moi", (HttpContext http, Banque banque) =>
        {
            var u = banque.Utilisateurs[Utilisateur(http)];
            return Results.Ok(new
            {
                u.Id, u.Prenom, u.Nom, u.Email, u.Segment, u.ConseillerId, u.Comptes,
                Adresse = "12 rue de l'Exemple, 75011 Paris", DateNaissance = new DateOnly(1990, 5, 17),
                Preferences = new { Langue = "fr", Notifications = true, Theme = "clair" }
            });
        });

        // ─── Comptes ───
        app.MapGet("/comptes/{id:guid}", (Guid id, HttpContext http, Banque banque) =>
            banque.Compte(id) is not { } compte ? Results.NotFound()
            : compte.Titulaire != Utilisateur(http) ? Interdit()
            : Results.Ok(compte));

        app.MapGet("/comptes/{id:guid}/operations", (Guid id, int? page, int? taille, HttpContext http, Banque banque) =>
        {
            if (banque.Compte(id) is not { } compte)
                return Results.NotFound();
            if (compte.Titulaire != Utilisateur(http))
                return Interdit();
            var (p, t) = (Math.Max(page ?? 1, 1), Math.Clamp(taille ?? 20, 1, 100));
            var (operations, total) = banque.Operations(id, p, t);
            return Results.Ok(new { page = p, taille = t, total, operations });
        });

        // ─── Conseillers, offres ───
        app.MapGet("/conseillers/{id}", (string id, Banque banque) =>
            banque.Conseillers.TryGetValue(id, out var c) ? Results.Ok(c) : Results.NotFound());

        app.MapGet("/offres", (string? segment, Banque banque, Chaos chaos) =>
            chaos.OffresEnPanne
                ? Results.Problem("Service des offres indisponible.", statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(banque.Offres.Where(o => segment is null || o.Segment == segment)));

        // ─── Virements ───
        app.MapPost("/virements", (VirementRequete r, HttpContext http, Banque banque, TimeProvider horloge) =>
        {
            if (banque.Compte(r.Source) is { } source && source.Titulaire != Utilisateur(http))
                return Interdit();
            try
            {
                var compte = banque.Virer(r.Source, r.Cible, r.Montant, horloge.GetUtcNow());
                return Results.Ok(new { compte.Id, compte.Solde });
            }
            catch (KeyNotFoundException e)
            {
                return Results.NotFound(new { erreur = e.Message });
            }
            catch (InvalidOperationException e)
            {
                return Results.BadRequest(new { erreur = e.Message });
            }
        });

        // ─── Démo ───
        app.MapGet("/chaos", (Chaos chaos) => chaos);
        app.MapPut("/chaos", (ChaosRequete r, Chaos chaos) =>
        {
            chaos.LatenceMs = r.LatenceMs ?? chaos.LatenceMs;
            chaos.OffresEnPanne = r.OffresEnPanne ?? chaos.OffresEnPanne;
            return Results.Ok(chaos);
        });
    }
}
