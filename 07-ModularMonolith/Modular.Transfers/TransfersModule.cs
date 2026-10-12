using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modular.Shared;

namespace Modular.Transfers;

public sealed class TransfersModule : IModule
{
    public string Name => "Transfers";

    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddDbContext<TransfersDbContext>((sp, options) => options.UseInMemoryDatabase(sp.DatabaseName(Name)));

    public void MapEndpoints(IEndpointRouteBuilder app) => TransfersEndpoints.Map(app);
}
