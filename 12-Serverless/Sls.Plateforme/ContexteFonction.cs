using System.Text.Json;

namespace Sls.Plateforme;

/// <summary>
/// Ce que la plateforme donne à une fonction pour une invocation : des informations sur l'exécution
/// et l'accès aux services gérés. C'est le « context » d'AWS Lambda ou le FunctionContext d'Azure.
/// </summary>
public sealed class ContexteFonction
{
    private readonly ServicesGeres _services;

    internal ContexteFonction(string nomFonction, string idInstance, bool demarrageAFroid,
        IDictionary<string, object> memoireInstance, ServicesGeres services, CancellationToken annulation)
    {
        (NomFonction, IdInstance, DemarrageAFroid, MemoireInstance, _services, Annulation) =
            (nomFonction, idInstance, demarrageAFroid, memoireInstance, services, annulation);
    }

    /// <summary>Pour tester une fonction directement, sans plateforme : c'est une simple méthode.</summary>
    public static ContexteFonction PourTests(ServicesGeres services, IDictionary<string, object>? memoireInstance = null) =>
        new("test", "test", false, memoireInstance ?? new Dictionary<string, object>(), services, CancellationToken.None);

    public string NomFonction { get; }
    public string IdInstance { get; }
    public bool DemarrageAFroid { get; }
    public Guid IdInvocation { get; } = Guid.NewGuid();

    /// <summary>Annulé quand le délai maximal de la fonction est atteint.</summary>
    public CancellationToken Annulation { get; }

    /// <summary>
    /// Mémoire de l'INSTANCE : conservée entre deux invocations servies par la même instance « chaude »,
    /// perdue au recyclage, et différente d'une instance à l'autre. Utile pour un cache, jamais pour une donnée.
    /// </summary>
    public IDictionary<string, object> MemoireInstance { get; }

    public TableGeree Table(string nom) => _services.Table(nom);

    public Task EnvoyerDansFile(string file, object message) =>
        _services.Files.Envoyer(file, JsonSerializer.Serialize(message, Json.Options));

    public Task EcrireObjet(string chemin, string contenu) => _services.Stockage.Ecrire(chemin, contenu);

    public string? Reglage(string cle) => _services.Reglages.GetValueOrDefault(cle);

    public void Journaliser(string message) => _services.Journal.Ecrire($"{NomFonction}#{IdInstance}", message);
}
