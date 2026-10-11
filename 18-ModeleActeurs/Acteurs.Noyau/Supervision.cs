namespace Acteurs.Noyau;

/// <summary>
/// Ce que fait le superviseur quand un acteur plante (« let it crash ») : on ne rattrape pas l'erreur dans l'acteur,
/// on jette l'incarnation fautive et on en démarre une neuve, qui recharge l'état durable.
/// Au-delà de <see cref="MaxRedemarrages"/> pannes dans la <see cref="Fenetre"/>, redémarrer ne sert plus à rien :
/// l'acteur est arrêté et refuse les messages jusqu'à la fin de la fenêtre (quarantaine).
/// </summary>
public sealed record StrategieSupervision(int MaxRedemarrages, TimeSpan Fenetre)
{
    public static readonly StrategieSupervision ParDefaut = new(3, TimeSpan.FromMinutes(1));
}

/// <summary>Le message a fait planter l'acteur : il a été redémarré (ou arrêté), le message est perdu.</summary>
public sealed class ActeurEnPanneException(string chemin, Exception cause)
    : Exception($"{chemin} a planté en traitant ce message : {cause.Message}", cause)
{
    public string Chemin { get; } = chemin;
}

/// <summary>L'acteur est en quarantaine : trop de pannes récentes.</summary>
public sealed class ActeurIndisponibleException(string chemin, DateTimeOffset jusquA)
    : Exception($"{chemin} est arrêté après des pannes répétées, indisponible jusqu'à {jusquA:HH:mm:ss}.")
{
    public string Chemin { get; } = chemin;
}

/// <summary>Photographie du système, pour l'observer de l'extérieur.</summary>
public sealed record EtatSysteme(
    int Actifs,
    long Activations,
    long Passivations,
    long Redemarrages,
    long Arrets,
    long LettresMortes,
    IReadOnlyList<EtatActivation> Acteurs);

public sealed record EtatActivation(string Chemin, DateTimeOffset ActiveLe, long MessagesTraites, int EnAttente, int Redemarrages);
