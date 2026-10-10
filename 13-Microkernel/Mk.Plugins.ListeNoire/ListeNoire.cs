using Mk.Contrats;

namespace Mk.Plugins.ListeNoire;

/// <summary>Point d'extension « contrôle » : interdit les virements vers des comptes listés dans plugin.json.</summary>
public sealed class ListeNoire : IControleOperation
{
    private HashSet<Guid> _comptesBloques = [];

    public string Nom => "ListeNoire";
    public string Description => "Refuse les virements vers les comptes de la liste noire.";

    public void Initialiser(ContextePlugin contexte)
    {
        _comptesBloques = (contexte.Reglage("comptesBloques") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Guid.Parse)
            .ToHashSet();
        contexte.Journaliser($"{_comptesBloques.Count} compte(s) en liste noire");
    }

    public string? Verifier(OperationProposee operation) =>
        operation.Type == TypeOperation.Virement && operation.CompteCible is { } cible && _comptesBloques.Contains(cible)
            ? "Virements vers ce compte interdits (liste noire)."
            : null;
}
