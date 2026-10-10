using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Onion.Domain.Interfaces;
using Onion.Domain.Model;

namespace Onion.Infrastructure;

public static class DependencyInjection
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string nomBase = "Banque")
    {
        services.AddDbContext<BanqueDbContext>(options => options.UseInMemoryDatabase(nomBase));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BanqueDbContext>());
        services.AddScoped<IComptesRepository, ComptesRepository>();
        return services;
    }

    public static async Task InitialiserDonneesDeDemo(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var comptes = scope.ServiceProvider.GetRequiredService<IComptesRepository>();
        if (!await comptes.EstVide())
            return;

        await comptes.Ajouter(new Compte(Alice, "Alice", 1000m));
        await comptes.Ajouter(new Compte(Bob, "Bob", 500m));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Enregistrer();
    }
}
