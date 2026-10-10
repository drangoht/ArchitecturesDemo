using Cqrs.Lecture.Modele;
using Cqrs.Lecture.Projections;
using Cqrs.Lecture.Requetes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Lecture;

public static class LectureExtensions
{
    public static IServiceCollection AddCoteLecture(this IServiceCollection services, Func<IServiceProvider, string> nomBase)
    {
        // Base distincte de celle de l'écriture : elle pourrait être d'une autre technologie (cache, moteur de recherche…).
        services.AddDbContext<LectureDb>((sp, options) => options.UseInMemoryDatabase(nomBase(sp)));
        services.AddScoped<Projecteur>();
        services.AddScoped<BusRequetes>();

        services.AddScoped<IGestionnaireRequete<ConsulterReleve, ReleveVue?>, ConsulterReleveGestionnaire>();
        services.AddScoped<IGestionnaireRequete<ListerOperations, IReadOnlyList<OperationVue>>, ListerOperationsGestionnaire>();
        services.AddScoped<IGestionnaireRequete<ConsulterTableauDeBord, TableauDeBordVue>, ConsulterTableauDeBordGestionnaire>();
        return services;
    }
}
