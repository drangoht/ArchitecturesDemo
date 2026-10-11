using Acteurs.Noyau;
using Microsoft.Extensions.Options;

namespace Acteurs.Api;

public sealed class OptionsActeurs
{
    /// <summary>Un acteur sans message depuis ce délai est passivé (sa mémoire libérée, son état durable conservé).</summary>
    public int InactiviteSecondes { get; set; } = 30;
    public int PeriodePassivationSecondes { get; set; } = 5;
    public int MaxRedemarrages { get; set; } = 3;
    public int FenetreSecondes { get; set; } = 60;

    /// <summary>Au-delà, une demande à un acteur répond 504 (le message, lui, reste dans la boîte).</summary>
    public int DelaiDemandeMs { get; set; } = 5_000;
}

/// <summary>Le « ramasse-miettes » des acteurs : passe régulièrement endormir ceux qui ne servent plus.</summary>
public sealed class PassivationAutomatique(SystemeActeurs systeme, IOptions<OptionsActeurs> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        try
        {
            await systeme.PassiverRegulierement(TimeSpan.FromSeconds(options.Value.InactiviteSecondes),
                TimeSpan.FromSeconds(options.Value.PeriodePassivationSecondes), arret);
        }
        catch (OperationCanceledException)
        {
            // arrêt de l'application
        }
    }
}
