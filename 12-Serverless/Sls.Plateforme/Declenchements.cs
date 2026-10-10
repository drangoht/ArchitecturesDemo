using Microsoft.Extensions.Hosting;

namespace Sls.Plateforme;

/// <summary>Lit les files et lance une invocation par message, en parallèle, tant qu'il y a des instances disponibles.</summary>
internal sealed class DistributeurFiles(MoteurExecution moteur, ServicesGeres services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        var abonnements = moteur.Definitions.Where(d => d.File is not null).ToList();
        while (!arret.IsCancellationRequested)
        {
            foreach (var definition in abonnements)
                while (moteur.PeutDemarrer(definition.Nom) && services.Files.Prendre(definition.File!) is { } message)
                    _ = Traiter(definition, message);
            await Task.Delay(20, arret);
        }
    }

    private async Task Traiter(DefinitionFonction definition, MessageEnFile message)
    {
        object declenchement = definition.Type == TypeDeclencheur.Stockage
            ? new ObjetStocke(message.Corps, services.Stockage.Lire(message.Corps) ?? "")
            : new MessageFile(message.Id, definition.File!, message.Corps, message.Tentative + 1);
        try
        {
            await moteur.Invoquer(definition.Nom, declenchement);
        }
        catch (EtranglementException)
        {
            services.Files.Remettre(definition.File!, message);
        }
        catch (Exception e)
        {
            services.Journal.Ecrire(definition.Nom, $"échec (tentative {message.Tentative + 1}) : {e.Message}");
            services.Files.Echec(definition.File!, message, e.Message);
        }
    }
}

/// <summary>Déclenche les fonctions planifiées (l'équivalent d'un cron).</summary>
internal sealed class Minuteries(MoteurExecution moteur, ServicesGeres services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        var prochaines = moteur.Definitions.Where(d => d.Type == TypeDeclencheur.Minuterie)
            .ToDictionary(d => d, d => DateTime.UtcNow + d.Periode!.Value);
        while (!arret.IsCancellationRequested)
        {
            foreach (var (definition, heure) in prochaines.ToList())
            {
                if (DateTime.UtcNow < heure)
                    continue;
                prochaines[definition] = heure + definition.Periode!.Value;
                _ = Declencher(moteur, services, definition.Nom);
            }
            await Task.Delay(100, arret);
        }
    }

    public static async Task Declencher(MoteurExecution moteur, ServicesGeres services, string nom)
    {
        try
        {
            await moteur.Invoquer(nom, new TicMinuterie(DateTime.UtcNow));
        }
        catch (Exception e)
        {
            services.Journal.Ecrire(nom, $"échec : {e.Message}");
        }
    }
}

internal sealed class Recycleur(MoteurExecution moteur) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        while (!arret.IsCancellationRequested)
        {
            moteur.Recycler();
            await Task.Delay(250, arret);
        }
    }
}
