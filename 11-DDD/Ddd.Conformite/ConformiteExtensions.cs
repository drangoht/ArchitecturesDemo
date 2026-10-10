using Ddd.TenueDeCompte.Contrats;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ddd.Conformite;

/// <summary>
/// Point de contact avec la Tenue de compte. Il TRADUIT le langage publié (VirementEmis) vers le modèle
/// de la Conformité (VirementObserve), puis applique les règles de vigilance.
/// </summary>
public class SurveillanceDesVirements(DossiersDeVigilance dossiers, ITenueDeCompteApi tenueDeCompte) : IAbonneTenueDeCompte
{
    public async Task SurVirementEmis(VirementEmis evenement)
    {
        var dossier = dossiers.Obtenir(evenement.CompteSourceId, evenement.TitulaireSource);
        IReadOnlyList<Alerte> alertes;
        lock (dossier)
            alertes = dossier.Observer(new VirementObserve(evenement.Date, evenement.MontantEnEuros, evenement.IbanBeneficiaire));

        if (alertes.FirstOrDefault(a => a.Niveau == NiveauAlerte.Critique) is { } critique)
            await tenueDeCompte.BloquerCompte(evenement.CompteSourceId, $"Blocage préventif (Conformité) : {critique.Motif}");
    }
}

public static class ConformiteExtensions
{
    public static IServiceCollection AddConformite(this IServiceCollection services)
    {
        services.AddSingleton<DossiersDeVigilance>();
        services.AddScoped<IAbonneTenueDeCompte, SurveillanceDesVirements>();
        return services;
    }

    /// <summary>L'API du contexte Conformité, avec son propre vocabulaire.</summary>
    public static void MapConformite(this IEndpointRouteBuilder app)
    {
        var conformite = app.MapGroup("/conformite");

        conformite.MapGet("/alertes", (DossiersDeVigilance dossiers) =>
            dossiers.Tous().SelectMany(d => d.Alertes).OrderByDescending(a => a.Date)
                .Select(a => new { a.CompteId, a.Titulaire, Niveau = a.Niveau.ToString(), a.Motif, a.Date }));

        conformite.MapGet("/dossiers/{compteId:guid}", (Guid compteId, DossiersDeVigilance dossiers) =>
            dossiers.Trouver(compteId) is { } d
                ? Results.Ok(new { d.CompteId, d.Titulaire, d.BlocageDemande, d.Virements, NombreAlertes = d.Alertes.Count })
                : Results.NotFound());
    }
}
