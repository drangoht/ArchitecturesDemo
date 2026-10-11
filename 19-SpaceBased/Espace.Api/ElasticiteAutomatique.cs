using Espace.Grille;
using Microsoft.Extensions.Options;

namespace Espace.Api;

public sealed class OptionsElasticite
{
    /// <summary>Fréquence des évaluations de la charge ; 0 = pas d'élasticité automatique (les tests la pilotent à la main).</summary>
    public int PeriodeEvaluationMs { get; set; } = 250;
}

/// <summary>Le gestionnaire de déploiement en action : il réévalue régulièrement la charge et ajuste le nombre d'unités.</summary>
public sealed class ElasticiteAutomatique(Plateforme plateforme, IOptions<OptionsElasticite> options, ILogger<ElasticiteAutomatique> journal)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        if (options.Value.PeriodeEvaluationMs <= 0)
            return;
        using var minuterie = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PeriodeEvaluationMs));
        try
        {
            while (await minuterie.WaitForNextTickAsync(arret))
                if (await plateforme.Deploiement.Evaluer() is { } decision)
                    journal.LogInformation("Élasticité : {Decision}", decision);
        }
        catch (OperationCanceledException)
        {
            // arrêt de l'application
        }
    }
}
