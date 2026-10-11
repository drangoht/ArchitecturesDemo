namespace Fig.Nouveau;

public sealed record BilanSynchronisation(int LignesLues, int FaitsAppliques, int Reportees, long PositionAncien);

/// <summary>
/// Tant que l'ancien système vit, les deux bases doivent dire la même chose :
/// ① ce que l'ancien écrit (y compris par des traitements qui ne passent pas par la façade) est rattrapé ici ;
/// ② ce que le nouveau service écrit est reporté dans l'ancien, pour ses routes pas encore migrées… et pour pouvoir
///    revenir en arrière à tout moment.
/// </summary>
public sealed class Synchronisation(Depot depot, ClientAncien ancien, ILogger<Synchronisation> journal)
{
    private const int TailleLot = 100;
    private readonly SemaphoreSlim _unSeulCycle = new(1, 1);

    public async Task<BilanSynchronisation> Executer(CancellationToken annulation = default)
    {
        await _unSeulCycle.WaitAsync(annulation);
        try
        {
            // ① Rattraper l'ancien, depuis la dernière position connue (le premier cycle recharge tout depuis le début).
            int lues = 0, appliques = 0;
            while (true)
            {
                var lignes = await ancien.Journal(depot.PositionAncien, TailleLot, annulation);
                if (lignes.Count == 0)
                    break;
                var faits = lignes.Select(TraducteurAncien.Traduire).ToList();
                appliques += depot.AppliquerDepuisAncien(faits, lignes[^1].Position);
                lues += lignes.Count;
                if (lignes.Count < TailleLot)
                    break;
            }

            // ② Reporter nos écritures. Si l'ancien ne répond pas, elles restent dans la boîte d'envoi pour le cycle
            //    suivant ; l'import étant idempotent, un lot renvoyé n'est pas compté deux fois.
            var aReporter = depot.AReporter();
            if (aReporter.Count > 0)
            {
                await ancien.Importer(aReporter.Select(TraducteurAncien.VersAncien).ToList(), annulation);
                depot.MarquerReportees(aReporter);
            }

            if (lues > 0 || aReporter.Count > 0)
                journal.LogInformation("Synchronisation : {Lues} lignes lues de l'ancien, {Appliques} faits appliqués, {Reportees} opérations reportées",
                    lues, appliques, aReporter.Count);
            return new BilanSynchronisation(lues, appliques, aReporter.Count, depot.PositionAncien);
        }
        finally
        {
            _unSeulCycle.Release();
        }
    }
}

/// <summary>Un cycle de synchronisation toutes les <c>Synchronisation:IntervalleMs</c> (0 : seulement à la demande, pour les tests).</summary>
public sealed class SynchronisationPeriodique(Synchronisation synchronisation, IConfiguration configuration,
    ILogger<SynchronisationPeriodique> journal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        var intervalle = configuration.GetValue("Synchronisation:IntervalleMs", 500);
        if (intervalle <= 0)
            return;
        while (!arret.IsCancellationRequested)
        {
            try
            {
                await synchronisation.Executer(arret);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !arret.IsCancellationRequested)
            {
                journal.LogWarning("Ancien système injoignable, nouvel essai dans {Intervalle} ms", intervalle);
            }
            catch (FormatException e)
            {
                journal.LogError(e, "Synchronisation arrêtée sur une ligne illisible");
            }
            await Task.Delay(intervalle, arret).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
