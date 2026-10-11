using Ihm.Banque;
using Ihm.Presentation;

namespace Ihm.Tests;

/// <summary>Une horloge arrêtée : le 11 octobre 2026 à 14 h 30, heure de Paris.</summary>
public sealed class HorlogeFixe : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 11, 12, 30, 0, TimeSpan.Zero);
}

/// <summary>
/// La vraie banque en mémoire, avec une télécommande : on peut RETENIR un appel (il attend qu'on le relâche) pour
/// observer l'écran pendant l'attente, ou pour faire arriver les réponses dans le désordre. Pas de Thread.Sleep.
/// </summary>
public sealed class BanqueTelecommandee : IBanque
{
    private readonly Dictionary<(string Appel, Guid? Compte), TaskCompletionSource> _retenues = [];

    public BanqueEnMemoire Reelle { get; } = new(new HorlogeFixe());

    public int VirementsDemandes { get; private set; }

    /// <summary>Le prochain appel « Virer » ou « Operations » (de ce compte) attendra <see cref="Relacher"/>.</summary>
    public void Retenir(string appel, Guid? compte = null)
    {
        lock (_retenues)
            _retenues[(appel, compte)] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Relacher(string appel, Guid? compte = null)
    {
        TaskCompletionSource? retenue;
        lock (_retenues)
            _retenues.Remove((appel, compte), out retenue);
        retenue?.SetResult();
    }

    private Task Passer(string appel, Guid? compte)
    {
        lock (_retenues)
            return _retenues.TryGetValue((appel, compte), out var retenue) ? retenue.Task : Task.CompletedTask;
    }

    public Task<IReadOnlyList<Compte>> ComptesAsync(CancellationToken annulation = default) => Reelle.ComptesAsync(annulation);

    public Task<Compte> CompteAsync(Guid id, CancellationToken annulation = default) => Reelle.CompteAsync(id, annulation);

    public async Task<IReadOnlyList<Operation>> OperationsAsync(Guid compte, CancellationToken annulation = default)
    {
        await Passer("Operations", compte);
        return await Reelle.OperationsAsync(compte, annulation);
    }

    public async Task<Virement> VirerAsync(Guid source, Guid cible, decimal montant, CancellationToken annulation = default)
    {
        VirementsDemandes++;
        await Passer("Virer", null);
        return await Reelle.VirerAsync(source, cible, montant, annulation);
    }
}

/// <summary>Une confirmation qui répond ce qu'on lui dit, et retient les questions posées.</summary>
public sealed class ConfirmationProgrammee(bool reponse) : IConfirmation
{
    public List<string> Questions { get; } = [];

    public Task<bool> DemanderAsync(string question)
    {
        Questions.Add(question);
        return Task.FromResult(reponse);
    }
}
