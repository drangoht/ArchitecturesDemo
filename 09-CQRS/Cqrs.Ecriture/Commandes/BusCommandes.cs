using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cqrs.Ecriture.Commandes;

/// <summary>Une intention de modifier l'état (à l'impératif). Elle ne renvoie pas de données.</summary>
public interface ICommande;

public interface IGestionnaireCommande<in TCommande> where TCommande : ICommande
{
    Task Traiter(TCommande commande);
}

/// <summary>Validation de forme d'une commande, exécutée AVANT le gestionnaire (les règles métier restent dans l'agrégat).</summary>
public interface IValidateur<in TCommande> where TCommande : ICommande
{
    IEnumerable<string> Valider(TCommande commande);
}

public class CommandeInvalideException(IReadOnlyList<string> erreurs) : Exception(string.Join(" ", erreurs))
{
    public IReadOnlyList<string> Erreurs { get; } = erreurs;
}

/// <summary>
/// Point d'entrée unique des commandes : un « pipeline » commun (validation, journalisation)
/// avant le gestionnaire. C'est le rôle que joue souvent MediatR, écrit ici en quelques lignes.
/// </summary>
public class BusCommandes(IServiceProvider services, ILogger<BusCommandes> journal)
{
    public async Task Envoyer<TCommande>(TCommande commande) where TCommande : ICommande
    {
        var erreurs = services.GetServices<IValidateur<TCommande>>()
            .SelectMany(validateur => validateur.Valider(commande))
            .ToList();
        if (erreurs.Count > 0)
            throw new CommandeInvalideException(erreurs);

        journal.LogInformation("Commande {Commande} : {Contenu}", typeof(TCommande).Name, commande);
        await services.GetRequiredService<IGestionnaireCommande<TCommande>>().Traiter(commande);
    }
}
