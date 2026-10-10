namespace Micro.Comptes;

internal static class Endpoints
{
    internal record RetraitRequete(decimal Montant);
    internal record MouvementRequete(string OperationId, decimal Montant);

    /// <summary>API publique, exposée aux clients par la passerelle.</summary>
    public static void MapEndpointsPublics(this IEndpointRouteBuilder app)
    {
        app.MapGet("/comptes/{id:guid}", async (Guid id, ComptesDb db) =>
            await db.Comptes.FindAsync(id) is { } c
                ? Results.Ok(new { c.Id, c.Titulaire, c.Solde })
                : Results.NotFound(new { erreur = $"Compte {id} introuvable." }));

        app.MapPost("/comptes/{id:guid}/retrait", async (Guid id, RetraitRequete requete, ComptesDb db) =>
        {
            var compte = await db.Comptes.FindAsync(id);
            if (compte is null)
                return Results.NotFound(new { erreur = $"Compte {id} introuvable." });
            if (compte.Debiter(requete.Montant) is { } refus)
                return Results.BadRequest(new { erreur = refus });

            // Modification ET événement enregistrés ensemble : soit les deux, soit aucun.
            db.Outbox.Add(MessageOutbox.Creer("retrait-effectue",
                new { compteId = id, montant = requete.Montant, nouveauSolde = compte.Solde }));
            await db.SaveChangesAsync();

            return Results.Ok(new { compte.Id, compte.Titulaire, compte.Solde });
        });
    }

    /// <summary>
    /// API interne, appelée par les autres services (la passerelle ne l'expose pas).
    /// Idempotente : un appel rejoué avec le même operationId (retry après une coupure réseau) ne débite pas deux fois.
    /// </summary>
    public static void MapEndpointsInternes(this IEndpointRouteBuilder app)
    {
        var interne = app.MapGroup("/interne/comptes/{id:guid}");
        interne.MapPost("/debits", (Guid id, MouvementRequete r, ComptesDb db) => Appliquer(id, r, db, (c, m) => c.Debiter(m)));
        interne.MapPost("/credits", (Guid id, MouvementRequete r, ComptesDb db) => Appliquer(id, r, db, (c, m) => c.Crediter(m)));
    }

    private static async Task<IResult> Appliquer(
        Guid id, MouvementRequete requete, ComptesDb db, Func<Compte, decimal, string?> mouvement)
    {
        if (await db.OperationsTraitees.FindAsync(requete.OperationId) is { } dejaTraitee)
            return Repondre(dejaTraitee);

        var compte = await db.Comptes.FindAsync(id);
        if (compte is null)
            return Results.NotFound(new { erreur = $"Compte {id} introuvable." });

        var refus = mouvement(compte, requete.Montant);
        var operation = new OperationTraitee
        {
            OperationId = requete.OperationId,
            Acceptee = refus is null,
            Erreur = refus,
            SoldeApres = compte.Solde
        };
        db.OperationsTraitees.Add(operation);
        await db.SaveChangesAsync();
        return Repondre(operation);
    }

    private static IResult Repondre(OperationTraitee operation) => operation.Acceptee
        ? Results.Ok(new { operation.OperationId, operation.SoldeApres })
        : Results.BadRequest(new { erreur = operation.Erreur });
}
