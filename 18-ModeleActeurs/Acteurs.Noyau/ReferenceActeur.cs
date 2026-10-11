namespace Acteurs.Noyau;

/// <summary>
/// L'ADRESSE d'un acteur, pas l'acteur lui-même : on peut la garder, la copier, la transmettre dans un message.
/// Qu'il soit actif, passivé ou en cours de redémarrage ne change rien pour l'appelant (transparence de localisation :
/// dans Orleans ou Akka, il pourrait même vivre sur une autre machine).
/// </summary>
public sealed record ReferenceActeur
{
    private readonly SystemeActeurs _systeme;

    internal ReferenceActeur(SystemeActeurs systeme, string chemin)
    {
        _systeme = systeme;
        Chemin = chemin;
    }

    public string Chemin { get; }

    /// <summary>« Tell » : déposer un message et passer à autre chose, sans attendre de réponse.</summary>
    public void Envoyer(object message) => _systeme.Deposer(Chemin, new Enveloppe(message, null));

    /// <summary>
    /// « Ask » : déposer un message et attendre la réponse.
    /// Lève <see cref="ActeurEnPanneException"/> si le message a fait planter l'acteur,
    /// <see cref="ActeurIndisponibleException"/> s'il est arrêté par son superviseur, <see cref="TimeoutException"/> s'il tarde trop.
    /// </summary>
    public async Task<object?> Demander(object message, TimeSpan? delai = null)
    {
        var reponse = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _systeme.Deposer(Chemin, new Enveloppe(message, reponse));
        var attente = delai ?? _systeme.DelaiDemandes;
        try
        {
            return await reponse.Task.WaitAsync(attente);
        }
        catch (TimeoutException)
        {
            // Le message reste dans la boîte et sera traité plus tard : seul l'appelant a cessé d'attendre.
            throw new TimeoutException($"{Chemin} n'a pas répondu dans les {attente.TotalMilliseconds:0} ms.");
        }
    }

    public override string ToString() => Chemin;
}

/// <summary>Un message et, pour une demande, de quoi y répondre.</summary>
internal sealed record Enveloppe(object Message, TaskCompletionSource<object?>? Reponse);
