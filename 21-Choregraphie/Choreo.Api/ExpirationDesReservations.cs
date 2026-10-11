using Choreo.Services;

namespace Choreo.Api;

public sealed class OptionsApi
{
    public int DureeReservationSecondes { get; set; } = 30;
    public bool Fidelite { get; set; } = true;
    public double ProbabiliteDoublon { get; set; }

    /// <summary>0 = pas d'expiration automatique (les tests la déclenchent à la main).</summary>
    public int PeriodeExpirationMs { get; set; } = 1_000;
}

/// <summary>La minuterie du service Comptes : elle libère les réservations que personne n'a conclues à temps.</summary>
public sealed class ExpirationDesReservations(Banque banque, OptionsApi options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        if (options.PeriodeExpirationMs <= 0)
            return;
        using var minuterie = new PeriodicTimer(TimeSpan.FromMilliseconds(options.PeriodeExpirationMs));
        try
        {
            while (await minuterie.WaitForNextTickAsync(arret))
                banque.Comptes.ExpirerReservations();
        }
        catch (OperationCanceledException)
        {
            // arrêt de l'application
        }
    }
}
