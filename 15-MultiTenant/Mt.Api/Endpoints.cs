using Mt.Banque;
using Mt.Locataires;

namespace Mt.Api;

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Alice et Bob existent chez CHAQUE banque, avec les mêmes identifiants mais des soldes différents :
    /// la même URL /comptes/1111… donne trois réponses selon le locataire.
    /// </summary>
    public static IEnumerable<Compte> Comptes(string locataire) => locataire switch
    {
        "alpha" => [new Compte(Alice, "Alice", 1_000m), new Compte(Bob, "Bob", 500m)],
        "beta" => [new Compte(Alice, "Alice", 3_000m), new Compte(Bob, "Bob", 200m)],
        "gamma" => [new Compte(Alice, "Alice", 25_000m), new Compte(Bob, "Bob", 1_500m)],
        _ => []
    };
}

internal static class Endpoints
{
    internal record OuvertureRequete(string Titulaire, decimal DepotInitial);
    internal record MontantRequete(decimal Montant);
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);
    internal record FormuleRequete(Plan Plan);
    internal record FraisRequete(string Mois);

    /// <summary>L'API des clients : chaque route s'exécute pour le locataire de la requête, sans jamais le mentionner.</summary>
    public static void MapBanque(this IEndpointRouteBuilder app)
    {
        app.MapGet("/locataire", (IContexteLocataire contexte) =>
        {
            var l = contexte.Exiger();
            return Results.Ok(new { l.Id, l.Nom, l.Plan, l.Isolation, l.Regles, l.Quotas });
        });

        app.MapGet("/comptes", async (ServiceBancaire banque) => Results.Ok(await banque.Lister()));

        app.MapPost("/comptes", async (OuvertureRequete r, ServiceBancaire banque) =>
        {
            var compte = await banque.Ouvrir(r.Titulaire, r.DepotInitial);
            return Results.Created($"/comptes/{compte.Id}", compte);
        });

        // Le compte d'une autre banque répond 404, comme un compte qui n'existe pas : on ne révèle même pas son existence.
        app.MapGet("/comptes/{id:guid}", async (Guid id, ServiceBancaire banque) =>
            await banque.Consulter(id) is { } compte ? Results.Ok(compte) : Results.NotFound());

        app.MapGet("/comptes/{id:guid}/operations", async (Guid id, ServiceBancaire banque) =>
            Results.Ok(await banque.Operations(id)));

        app.MapPost("/comptes/{id:guid}/retrait", async (Guid id, MontantRequete r, ServiceBancaire banque) =>
            Results.Ok(await banque.Retirer(id, r.Montant)));

        app.MapPost("/virements", async (VirementRequete r, ServiceBancaire banque) =>
            Results.Ok(await banque.Virer(r.Source, r.Cible, r.Montant)));
    }

    /// <summary>Le back-office de l'éditeur (hors locataire).</summary>
    public static void MapPlateforme(this IEndpointRouteBuilder app)
    {
        var plateforme = app.MapGroup("/plateforme");

        plateforme.MapGet("/locataires", (CatalogueLocataires catalogue) =>
            Results.Ok(catalogue.Tous().Select(Vue)));

        plateforme.MapGet("/locataires/{id}", (string id, CatalogueLocataires catalogue) =>
            catalogue.Trouver(id) is { } l ? Results.Ok(Vue(l)) : Results.NotFound());

        plateforme.MapPost("/locataires", async (DefinitionLocataire d, ServicePlateforme service) =>
        {
            var locataire = await service.Inscrire(d);
            return Results.Created($"/plateforme/locataires/{locataire.Id}", Vue(locataire));
        });

        plateforme.MapPost("/locataires/{id}/suspendre", (string id, ServicePlateforme service) =>
            service.Suspendre(id, true) ? Results.NoContent() : Results.NotFound());

        plateforme.MapPost("/locataires/{id}/reactiver", (string id, ServicePlateforme service) =>
            service.Suspendre(id, false) ? Results.NoContent() : Results.NotFound());

        plateforme.MapPost("/locataires/{id}/formule", async (string id, FormuleRequete r, ServicePlateforme service) =>
            await service.ChangerDeFormule(id, r.Plan) is { } bilan ? Results.Ok(bilan) : Results.NotFound());

        plateforme.MapDelete("/locataires/{id}", async (string id, ServicePlateforme service) =>
            await service.Supprimer(id) is { } bilan ? Results.Ok(bilan) : Results.NotFound());

        plateforme.MapGet("/stockage", async (ServicePlateforme service) => Results.Ok(await service.Inventaire()));

        plateforme.MapPost("/taches/frais-mensuels", async (FraisRequete r, ServicePlateforme service) =>
            Results.Ok(await service.PreleverFraisMensuels(r.Mois)));
    }

    private static object Vue(Locataire l) =>
        new { l.Id, l.Nom, l.Plan, l.Isolation, Base = FabriqueBanque.NomBase(l), l.Actif, l.Regles, l.Quotas };
}
