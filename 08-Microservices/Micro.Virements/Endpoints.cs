using Polly;

namespace Micro.Virements;

internal static class Endpoints
{
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);

    public static void MapVirements(this IEndpointRouteBuilder app)
    {
        app.MapPost("/virements", Virer);
        app.MapGet("/virements/{id:guid}", async (Guid id, VirementsDb db) =>
            await db.Virements.FindAsync(id) is { } v ? Results.Ok(Vue(v)) : Results.NotFound());
    }

    private static async Task<IResult> Virer(VirementRequete requete, ClientComptes comptes, VirementsDb db)
    {
        if (requete.Montant <= 0)
            return Results.BadRequest(new { erreur = "Le montant doit être strictement positif." });
        if (requete.Source == requete.Cible)
            return Results.BadRequest(new { erreur = "Impossible de virer vers le même compte." });

        var virement = new Virement { SourceId = requete.Source, CibleId = requete.Cible, Montant = requete.Montant };
        db.Virements.Add(virement);
        await db.SaveChangesAsync();

        // Étape 1 : vérifier les comptes et débiter la source.
        CompteDistant? source, cible;
        try
        {
            source = await comptes.Consulter(requete.Source);
            cible = await comptes.Consulter(requete.Cible);
            if (source is null || cible is null)
                return await Terminer(db, virement, StatutVirement.Refuse, "Compte source ou cible introuvable.", 404);

            var debit = await comptes.Debiter(source.Id, requete.Montant, $"{virement.Id}-debit");
            if (debit.Issue != Issue.Accepte)
                return await Terminer(db, virement, StatutVirement.Refuse, debit.Erreur, 400);
        }
        catch (Exception e) when (EstUnePanneReseau(e))
        {
            // Le débit a peut-être été appliqué (réponse perdue). Grâce à l'operationId,
            // une reprise pourra rejouer l'appel sans risque de double débit.
            return await Terminer(db, virement, StatutVirement.AReprendre, "Service Comptes indisponible.", 503);
        }

        // Étape 2 : créditer la cible. En cas d'échec : compensation (on recrédite la source).
        string? echecCredit;
        try
        {
            var credit = await comptes.Crediter(cible.Id, requete.Montant, $"{virement.Id}-credit");
            echecCredit = credit.Issue == Issue.Accepte ? null : credit.Erreur;
        }
        catch (Exception e) when (EstUnePanneReseau(e))
        {
            echecCredit = "Service Comptes indisponible.";
        }

        if (echecCredit is not null)
        {
            try
            {
                await comptes.Crediter(source.Id, requete.Montant, $"{virement.Id}-compensation");
                return await Terminer(db, virement, StatutVirement.Annule, $"{echecCredit} Débit annulé.", 400);
            }
            catch (Exception e) when (EstUnePanneReseau(e))
            {
                return await Terminer(db, virement, StatutVirement.AReprendre,
                    "Crédit et compensation impossibles : virement à reprendre.", 503);
            }
        }

        // Étape 3 : succès. Statut ET événement enregistrés dans la même transaction (outbox).
        db.Outbox.Add(MessageOutbox.Creer("virement-effectue", new
        {
            virementId = virement.Id,
            sourceId = source.Id,
            titulaireSource = source.Titulaire,
            cibleId = cible.Id,
            titulaireCible = cible.Titulaire,
            montant = requete.Montant
        }));
        return await Terminer(db, virement, StatutVirement.Effectue, null, 200);
    }

    private static bool EstUnePanneReseau(Exception e) =>
        e is HttpRequestException or TaskCanceledException or ExecutionRejectedException;

    private static async Task<IResult> Terminer(
        VirementsDb db, Virement virement, StatutVirement statut, string? erreur, int codeHttp)
    {
        virement.Statut = statut;
        virement.Erreur = erreur;
        await db.SaveChangesAsync();
        return Results.Json(Vue(virement), statusCode: codeHttp);
    }

    private static object Vue(Virement v) =>
        new { virementId = v.Id, statut = v.Statut.ToString(), v.Montant, erreur = v.Erreur };
}
