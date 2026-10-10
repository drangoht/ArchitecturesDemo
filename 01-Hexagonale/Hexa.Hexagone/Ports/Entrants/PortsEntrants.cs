namespace Hexa.Hexagone.Ports.Entrants;

// Ports entrants (« driving », primaires) : ce que l'application OFFRE au monde extérieur.
// Ils sont appelés par les adapters entrants : API web, ligne de commande, tests…

public interface IConsulterCompte
{
    Task<CompteVue> Executer(Guid compteId);
}

public interface IRetirerArgent
{
    Task Executer(Guid compteId, decimal montant);
}

public interface IEffectuerVirement
{
    Task Executer(Guid sourceId, Guid cibleId, decimal montant);
}

/// <summary>Ce qu'un consommateur du port voit d'un compte (et non l'entité elle-même).</summary>
public record CompteVue(Guid Id, string Titulaire, decimal Solde);
