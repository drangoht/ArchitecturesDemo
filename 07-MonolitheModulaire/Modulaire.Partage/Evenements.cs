using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modulaire.Partage;

/// <summary>Marqueur des événements publiés par un module à destination des autres.</summary>
public interface IEvenementIntegration;

public interface IBusEvenements
{
    Task Publier<T>(T evenement) where T : IEvenementIntegration;
}

/// <summary>Implémenté par un module qui veut réagir à un événement d'un autre module.</summary>
public interface IGestionnaireEvenement<in T> where T : IEvenementIntegration
{
    Task Traiter(T evenement);
}

/// <summary>
/// Bus en mémoire, dans le même processus. Chaque gestionnaire tourne dans son propre scope,
/// et une erreur dans un module abonné ne fait pas échouer le module qui publie.
/// En production : une « outbox » (événement enregistré dans la même transaction que les données)
/// puis un traitement asynchrone, pour ne perdre aucun événement.
/// </summary>
internal sealed class BusEnMemoire(IServiceScopeFactory scopes, ILogger<BusEnMemoire> journal) : IBusEvenements
{
    public async Task Publier<T>(T evenement) where T : IEvenementIntegration
    {
        using var scope = scopes.CreateScope();
        foreach (var gestionnaire in scope.ServiceProvider.GetServices<IGestionnaireEvenement<T>>())
        {
            try
            {
                await gestionnaire.Traiter(evenement);
            }
            catch (Exception e)
            {
                journal.LogError(e, "Le gestionnaire {Gestionnaire} a échoué sur {Evenement}",
                    gestionnaire.GetType().Name, typeof(T).Name);
            }
        }
    }
}

public static class BusExtensions
{
    public static IServiceCollection AddBusEvenements(this IServiceCollection services) =>
        services.AddSingleton<IBusEvenements, BusEnMemoire>();
}
