using Hexa.Hexagone.Ports.Sortants;

namespace Hexa.Tests.Hexagone;

/// <summary>Double de test du port sortant INotifications : enregistre ce qu'on lui demande.</summary>
public class NotificationsEspion : INotifications
{
    public List<string> Envoyees { get; } = [];

    public Task RetraitEffectue(Guid compteId, decimal montant)
    {
        Envoyees.Add($"retrait {montant} {compteId}");
        return Task.CompletedTask;
    }

    public Task VirementEffectue(Guid sourceId, Guid cibleId, decimal montant)
    {
        Envoyees.Add($"virement {montant} {sourceId} -> {cibleId}");
        return Task.CompletedTask;
    }
}
