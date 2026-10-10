using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couches.Donnees;

public static class DonneesExtensions
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static IServiceCollection AddCoucheDonnees(this IServiceCollection services, string nomBase)
    {
        services.AddDbContext<BanqueDbContext>(options => options.UseInMemoryDatabase(nomBase));
        services.AddScoped<IComptesDao, ComptesDao>();
        return services;
    }

    public static async Task InitialiserDonneesDeDemo(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dao = scope.ServiceProvider.GetRequiredService<IComptesDao>();
        if (!await dao.EstVide())
            return;

        await dao.Ajouter(new CompteEntite { Id = Alice, Titulaire = "Alice", Solde = 1000m });
        await dao.Ajouter(new CompteEntite { Id = Bob, Titulaire = "Bob", Solde = 500m });
        await dao.Enregistrer();
    }
}
