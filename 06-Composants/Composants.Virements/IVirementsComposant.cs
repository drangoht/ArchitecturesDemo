using Composants.Comptes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Composants.Virements;

// ─── Surface publique du composant ───

public interface IVirementsComposant
{
    Task<Resultat> Virer(Guid sourceId, Guid cibleId, decimal montant);
    Task<IReadOnlyList<VirementDto>> Historique(Guid compteId);
}

public record VirementDto(Guid SourceId, Guid CibleId, decimal Montant, DateTime DateUtc);

public static class VirementsExtensions
{
    /// <remarks>Nécessite que le composant Comptes soit aussi enregistré (AddComposantComptes).</remarks>
    public static IServiceCollection AddComposantVirements(this IServiceCollection services, string nomBase = "Virements")
    {
        services.AddDbContext<VirementsDbContext>(options => options.UseInMemoryDatabase(nomBase));
        services.AddScoped<IVirementsComposant, VirementsComposant>();
        return services;
    }
}
