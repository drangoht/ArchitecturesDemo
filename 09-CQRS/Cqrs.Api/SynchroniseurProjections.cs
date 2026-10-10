using Cqrs.Evenements;
using Cqrs.Lecture.Projections;

namespace Cqrs.Api;

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

/// <summary>
/// Lit les nouveaux événements du côté écriture et les applique au côté lecture.
/// Tourne en tâche de fond : la lecture est « en retard » d'un instant sur l'écriture (cohérence à terme).
/// Le réglage Projections:DelaiMs ralentit volontairement la projection pour rendre ce retard visible.
/// </summary>
internal sealed class SynchroniseurProjections(
    IServiceScopeFactory scopes, IConfiguration configuration, ILogger<SynchroniseurProjections> journal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        var delai = TimeSpan.FromMilliseconds(configuration.GetValue("Projections:DelaiMs", 0));

        while (!arret.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var flux = scope.ServiceProvider.GetRequiredService<IFluxEvenements>();
            var projecteur = scope.ServiceProvider.GetRequiredService<Projecteur>();

            var nouveaux = await flux.LireApres(await projecteur.DerniereSequence(), maximum: 100);
            if (nouveaux.Count == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), arret);
                continue;
            }

            await Task.Delay(delai, arret);
            foreach (var evenement in nouveaux)
            {
                await projecteur.Appliquer(evenement);
                journal.LogInformation("Projeté #{Sequence} {Type}", evenement.Sequence, evenement.Evenement.GetType().Name);
            }
        }
    }
}
