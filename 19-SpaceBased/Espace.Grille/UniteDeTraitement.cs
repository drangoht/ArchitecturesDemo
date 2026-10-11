namespace Espace.Grille;

/// <summary>Une requête métier. Sa <see cref="Cle"/> (ex. le compte) sert à la grille de messages pour choisir l'unité.</summary>
public interface IRequete
{
    string Cle { get; }
}

/// <summary>
/// Une requête envoyée par une unité à une autre (ex. le crédit d'un virement) : elle ne prend pas de place dans la file,
/// sinon deux unités pleines qui s'attendent l'une l'autre se bloqueraient.
/// </summary>
public interface IRequeteInterne : IRequete;

public sealed class UniteEnPanneException(string unite) : Exception($"L'unité {unite} est en panne.");

/// <summary>Le temps de traitement simulé d'une requête (et, pour les tests, une porte pour retenir les requêtes en cours).</summary>
public sealed class SimulationCharge
{
    private volatile TaskCompletionSource? _porte;

    public TimeSpan TempsDeTraitement { get; set; }

    public void Fermer() => _porte = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Ouvrir()
    {
        _porte?.TrySetResult();
        _porte = null;
    }

    internal async Task Passer()
    {
        if (_porte is { } porte)
            await porte.Task;
        if (TempsDeTraitement > TimeSpan.Zero)
            await Task.Delay(TempsDeTraitement);
    }
}

/// <summary>Ce que le métier peut faire depuis une unité : lire et écrire SA réplique, envoyer une requête à une autre unité.</summary>
public sealed class ContexteUnite(UniteDeTraitement unite, GrilleDeMessages messages)
{
    public string Unite => unite.Id;

    public T? Lire<T>(string cle) where T : class => unite.Replique.Lire(cle)?.Valeur as T;

    public object Modifier<T>(string cle, Func<T?, (T? Nouveau, object Reponse)> decider) where T : class
    {
        unite.VerifierEnVie();
        return unite.Replique.Modifier(cle, decider).Reponse;
    }

    public async Task<object> Envoyer(IRequeteInterne requete) => (await messages.Envoyer(requete)).Reponse;
}

/// <summary>
/// Une unité de traitement : le code métier ET une réplique complète des données, en mémoire. Elle se suffit à elle-même
/// pour répondre : ni base, ni autre unité à attendre. On en ajoute autant qu'il faut quand la charge monte.
/// </summary>
public sealed class UniteDeTraitement
{
    private readonly SemaphoreSlim _places;
    private readonly Func<object, ContexteUnite, Task<object>> _traitement;
    private readonly SimulationCharge _simulation;
    private readonly ContexteUnite _contexte;
    private int _enCours, _enAttente;
    private long _traitees;
    private volatile bool _enVie = true;

    internal UniteDeTraitement(string id, int capacite, Replique replique, Func<object, ContexteUnite, Task<object>> traitement,
        SimulationCharge simulation, GrilleDeMessages messages)
    {
        Id = id;
        Capacite = capacite;
        Replique = replique;
        _places = new SemaphoreSlim(capacite);
        _traitement = traitement;
        _simulation = simulation;
        _contexte = new ContexteUnite(this, messages);
    }

    public string Id { get; }
    public int Capacite { get; }
    public Replique Replique { get; }
    public int EnCours => Volatile.Read(ref _enCours);
    public int EnAttente => Volatile.Read(ref _enAttente);
    public long Traitees => Interlocked.Read(ref _traitees);

    /// <summary>Requêtes en cours ou en attente, rapportées à la capacité : 1 = pleine.</summary>
    public double Charge => (double)(EnCours + EnAttente) / Capacite;

    internal async Task<object> Traiter(object requete)
    {
        var interne = requete is IRequeteInterne;
        if (!interne)
        {
            Interlocked.Increment(ref _enAttente);
            await _places.WaitAsync();
            Interlocked.Decrement(ref _enAttente);
        }
        Interlocked.Increment(ref _enCours);
        try
        {
            VerifierEnVie();
            if (!interne)
                await _simulation.Passer();
            return await _traitement(requete, _contexte);
        }
        finally
        {
            Interlocked.Decrement(ref _enCours);
            Interlocked.Increment(ref _traitees);
            if (!interne)
                _places.Release();
        }
    }

    internal void VerifierEnVie()
    {
        if (!_enVie)
            throw new UniteEnPanneException(Id);
    }

    internal void Tuer() => _enVie = false;

    /// <summary>Attend la fin des requêtes en cours (arrêt propre).</summary>
    internal async Task Vider()
    {
        while (EnCours + EnAttente > 0)
            await Task.Delay(5);
    }
}
