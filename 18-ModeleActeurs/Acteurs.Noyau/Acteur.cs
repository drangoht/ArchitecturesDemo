namespace Acteurs.Noyau;

/// <summary>
/// Un acteur : un état PRIVÉ et une méthode qui traite UN message à la fois.
/// Personne d'autre ne touche à ses champs ; on ne lui parle qu'en déposant un message dans sa boîte aux lettres
/// (via une <see cref="ReferenceActeur"/>). D'où l'absence totale de verrou dans les classes dérivées.
/// </summary>
public abstract class Acteur
{
    /// <summary>Renseigné par le runtime avant <see cref="Demarrer"/>.</summary>
    public ContexteActeur Contexte { get; internal set; } = null!;

    /// <summary>
    /// Appelé à chaque incarnation (première activation, réactivation après passivation, redémarrage après une panne),
    /// avant le premier message : c'est là qu'on recharge l'état durable.
    /// </summary>
    public virtual Task Demarrer() => Task.CompletedTask;

    /// <summary>
    /// Traite un message et renvoie la réponse (ignorée si l'expéditeur n'en attend pas).
    /// Un refus métier est une réponse comme une autre ; une exception est une PANNE, confiée au superviseur.
    /// </summary>
    public abstract Task<object?> Recevoir(object message);

    /// <summary>Appelé quand l'incarnation s'arrête (passivation, redémarrage, arrêt définitif).</summary>
    public virtual Task Arreter() => Task.CompletedTask;
}

/// <summary>Ce que l'acteur sait de lui-même et du monde.</summary>
public sealed class ContexteActeur(SystemeActeurs systeme, string type, string cle)
{
    public SystemeActeurs Systeme { get; } = systeme;
    public string Type { get; } = type;

    /// <summary>L'identité de l'acteur dans son type (ex. l'identifiant du compte).</summary>
    public string Cle { get; } = cle;

    public string Chemin => $"{Type}/{Cle}";

    /// <summary>Pour parler à un autre acteur : on n'obtient jamais l'objet, seulement son adresse.</summary>
    public ReferenceActeur Acteur(string type, string cle) => Systeme.Acteur(type, cle);
}
