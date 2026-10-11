using System.Collections.Concurrent;

namespace Acteurs.Banque;

/// <summary>Où, dans le traitement d'un message, déclencher la panne.</summary>
public enum Etape
{
    /// <summary>Dès la réception, avant toute modification.</summary>
    Reception,

    /// <summary>Le compte a modifié son état EN MÉMOIRE mais pas encore écrit dans le stockage : le pire moment.</summary>
    AvantSauvegarde,

    /// <summary>Le virement a débité la source mais pas encore crédité la cible.</summary>
    ApresDebit
}

public enum Nature { Plantage, Lenteur }

public sealed record Piege(string Acteur, Etape Etape, Nature Nature = Nature.Plantage, int Fois = 1, int LatenceMs = 0);

public sealed class PanneSimuleeException(string message) : Exception(message);

/// <summary>Injecteur de pannes pour la démo (comme le Chaos des démos 08 et 14) : armé par l'API ou les tests.</summary>
public sealed class Pieges
{
    private readonly ConcurrentDictionary<(string Acteur, Etape Etape), Piege> _armes = new();

    public void Armer(Piege piege) => _armes[(piege.Acteur, piege.Etape)] = piege;

    public void ToutDesarmer() => _armes.Clear();

    public IReadOnlyList<Piege> Armes() => _armes.Values.ToList();

    /// <summary>Appelé par les acteurs aux étapes sensibles : ne fait rien, sauf si un piège est armé à cet endroit.</summary>
    public async Task Verifier(string acteur, Etape etape)
    {
        if (!_armes.TryGetValue((acteur, etape), out var piege))
            return;
        if (piege.Fois <= 1)
            _armes.TryRemove((acteur, etape), out _);
        else
            _armes[(acteur, etape)] = piege with { Fois = piege.Fois - 1 };

        if (piege.Nature == Nature.Lenteur)
            await Task.Delay(piege.LatenceMs);
        else
            throw new PanneSimuleeException($"panne simulée ({etape})");
    }
}
