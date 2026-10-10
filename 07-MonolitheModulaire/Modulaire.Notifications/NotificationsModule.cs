using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulaire.Comptes.Contrats;
using Modulaire.Partage;
using Modulaire.Virements.Contrats;

namespace Modulaire.Notifications;

public sealed class NotificationsModule : IModule
{
    public string Nom => "Notifications";

    public void Enregistrer(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((sp, options) => options.UseInMemoryDatabase(sp.NomBase(Nom)));

        // Abonnements aux événements des autres modules.
        services.AddScoped<IGestionnaireEvenement<RetraitEffectue>, SurRetraitEffectue>();
        services.AddScoped<IGestionnaireEvenement<VirementEffectue>, SurVirementEffectue>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/notifications/{compteId:guid}", async (Guid compteId, NotificationsDbContext db) =>
            Results.Ok(await db.Notifications
                .Where(n => n.CompteId == compteId)
                .OrderByDescending(n => n.DateUtc)
                .Select(n => new { n.Message, n.DateUtc })
                .ToListAsync()));
}
