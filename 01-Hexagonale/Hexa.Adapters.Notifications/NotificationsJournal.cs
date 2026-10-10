using Hexa.Hexagone.Ports.Sortants;

namespace Hexa.Adapters.Notifications;

/// <summary>Adapter sortant : écrit les notifications dans un flux texte (la console, un fichier…).</summary>
public class NotificationsJournal(TextWriter sortie) : INotifications
{
    public Task RetraitEffectue(Guid compteId, decimal montant) =>
        sortie.WriteLineAsync($"[notification] Retrait de {montant:0.00} € sur le compte {compteId}");

    public Task VirementEffectue(Guid sourceId, Guid cibleId, decimal montant) =>
        sortie.WriteLineAsync($"[notification] Virement de {montant:0.00} € de {sourceId} vers {cibleId}");
}
