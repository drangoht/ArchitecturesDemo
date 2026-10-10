using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Modulaire.Comptes.Contrats;
using Modulaire.Partage;
using Modulaire.Virements.Contrats;

namespace Modulaire.Virements;

internal static class VirementsEndpoints
{
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);

    public static void Map(IEndpointRouteBuilder app)
    {
        var virements = app.MapGroup("/virements");
        virements.MapPost("", Virer);
        virements.MapGet("/compte/{id:guid}", Historique);
    }

    private static async Task<IResult> Virer(
        VirementRequete requete, IComptesApi comptes, VirementsDbContext db, IBusEvenements bus)
    {
        if (requete.Montant <= 0)
            return Results.BadRequest(new { erreur = "Le montant doit être strictement positif." });
        if (requete.Source == requete.Cible)
            return Results.BadRequest(new { erreur = "Impossible de virer vers le même compte." });

        // Appel synchrone à un autre module, uniquement par son contrat public.
        var source = await comptes.Consulter(requete.Source);
        var cible = await comptes.Consulter(requete.Cible);
        if (source is null || cible is null)
            return Results.NotFound(new { erreur = "Compte source ou cible introuvable." });

        var debit = await comptes.Debiter(source.Id, requete.Montant);
        if (!debit.Succes)
            return Results.BadRequest(new { erreur = debit.Erreur });

        var credit = await comptes.Crediter(cible.Id, requete.Montant);
        if (!credit.Succes)
        {
            // Chaque module a sa base : pas de transaction commune, donc on compense le débit.
            await comptes.Crediter(source.Id, requete.Montant);
            return Results.BadRequest(new { erreur = credit.Erreur });
        }

        db.Virements.Add(new Virement { SourceId = source.Id, CibleId = cible.Id, Montant = requete.Montant });
        await db.SaveChangesAsync();

        // Communication asynchrone : on annonce le virement à qui veut l'entendre.
        await bus.Publier(new VirementEffectue(source.Id, source.Titulaire, cible.Id, cible.Titulaire, requete.Montant));

        return Results.Ok(new { montant = requete.Montant, de = source.Titulaire, vers = cible.Titulaire });
    }

    private static async Task<IResult> Historique(Guid id, VirementsDbContext db) =>
        Results.Ok(await db.Virements
            .Where(v => v.SourceId == id || v.CibleId == id)
            .OrderByDescending(v => v.DateUtc)
            .Select(v => new { v.SourceId, v.CibleId, v.Montant, v.DateUtc })
            .ToListAsync());
}
