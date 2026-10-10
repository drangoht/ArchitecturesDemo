using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulaire.Comptes.Contrats;
using Modulaire.Partage;

namespace Modulaire.Comptes;

/// <summary>L'API HTTP appartient au module (contrairement à 06-Composants, où le web est une couche à part).</summary>
internal static class ComptesEndpoints
{
    internal record RetraitRequete(decimal Montant);

    public static void Map(IEndpointRouteBuilder app)
    {
        var comptes = app.MapGroup("/comptes");

        comptes.MapGet("/{id:guid}", async (Guid id, IComptesApi api) =>
            await api.Consulter(id) is { } compte ? Results.Ok(compte) : Results.NotFound());

        comptes.MapPost("/{id:guid}/retrait", async (Guid id, RetraitRequete requete, IComptesApi api, IBusEvenements bus) =>
        {
            var resultat = await api.Debiter(id, requete.Montant);
            if (!resultat.Succes)
                return EnErreur(resultat);

            var compte = (await api.Consulter(id))!;
            // Le module annonce ce qui s'est passé ; il ne sait pas qui écoute (ici : Notifications).
            await bus.Publier(new RetraitEffectue(id, requete.Montant, compte.Solde));
            return Results.Ok(compte);
        });
    }

    private static IResult EnErreur(ResultatOperation resultat) => resultat.Statut == StatutOperation.Introuvable
        ? Results.NotFound(new { erreur = resultat.Erreur })
        : Results.BadRequest(new { erreur = resultat.Erreur });
}
