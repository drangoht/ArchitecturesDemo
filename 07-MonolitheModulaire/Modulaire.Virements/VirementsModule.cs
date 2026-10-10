using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulaire.Partage;

namespace Modulaire.Virements;

public sealed class VirementsModule : IModule
{
    public string Nom => "Virements";

    public void Enregistrer(IServiceCollection services, IConfiguration configuration) =>
        services.AddDbContext<VirementsDbContext>((sp, options) => options.UseInMemoryDatabase(sp.NomBase(Nom)));

    public void MapEndpoints(IEndpointRouteBuilder app) => VirementsEndpoints.Map(app);
}
