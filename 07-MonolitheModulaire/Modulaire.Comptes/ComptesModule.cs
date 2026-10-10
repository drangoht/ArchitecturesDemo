using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulaire.Comptes.Contrats;
using Modulaire.Partage;

namespace Modulaire.Comptes;

public sealed class ComptesModule : IModule
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public string Nom => "Comptes";

    public void Enregistrer(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ComptesDbContext>((sp, options) => options.UseInMemoryDatabase(sp.NomBase(Nom)));
        services.AddScoped<IComptesApi, ComptesApi>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => ComptesEndpoints.Map(app);

    public async Task Initialiser(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComptesDbContext>();
        if (await db.Comptes.AnyAsync())
            return;
        db.Comptes.AddRange(new Compte(Alice, "Alice", 1000m), new Compte(Bob, "Bob", 500m));
        await db.SaveChangesAsync();
    }
}
