using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Acteurs.Noyau;

/// <summary>
/// Le système d'acteurs : il crée les acteurs à la demande, leur distribue les messages, les supervise et
/// les endort quand ils ne servent plus.
/// <para>
/// Acteurs VIRTUELS (le modèle d'Orleans) : un acteur « compte/42 » existe toujours en principe ; on n'a ni à le créer
/// ni à le détruire. Le premier message l'active (une <i>incarnation</i> en mémoire), l'inactivité le passive,
/// le message suivant le réactive. Garantie centrale : au plus UNE incarnation par chemin à la fois.
/// </para>
/// </summary>
public sealed class SystemeActeurs(TimeProvider? horloge = null)
{
    private readonly TimeProvider _horloge = horloge ?? TimeProvider.System;
    private readonly Dictionary<string, TypeActeur> _types = new();

    // Tout ce qui décide de QUELLE incarnation reçoit un message est protégé par ce verrou (le runtime en a besoin ;
    // les acteurs, eux, n'en ont jamais).
    private readonly Lock _verrou = new();
    private readonly Dictionary<string, Activation> _activations = new();
    private readonly Dictionary<string, Task> _incarnationsFinissantes = new();
    private readonly Dictionary<string, DateTimeOffset> _quarantaines = new();

    private readonly ConcurrentQueue<string> _journal = new();
    private long _activationsTotales, _passivations, _redemarrages, _arrets, _lettresMortes;

    /// <summary>Délai par défaut d'une demande : un acteur qui ne répond pas ne bloque pas l'appelant indéfiniment.</summary>
    public TimeSpan DelaiDemandes { get; init; } = TimeSpan.FromSeconds(5);

    private sealed record TypeActeur(Func<Acteur> Fabrique, StrategieSupervision Strategie);

    /// <summary>Déclare un type d'acteur : « compte » → comment fabriquer une incarnation, comment la superviser.</summary>
    public void Enregistrer(string type, Func<Acteur> fabrique, StrategieSupervision? strategie = null) =>
        _types[type] = new TypeActeur(fabrique, strategie ?? StrategieSupervision.ParDefaut);

    /// <summary>L'adresse d'un acteur. Ne l'active pas : seul un message le fait.</summary>
    public ReferenceActeur Acteur(string type, string cle)
    {
        if (!_types.ContainsKey(type))
            throw new ArgumentException($"Type d'acteur inconnu : « {type} ».", nameof(type));
        return new ReferenceActeur(this, $"{type}/{cle}");
    }

    public ReferenceActeur Acteur(string type, Guid cle) => Acteur(type, cle.ToString());

    /// <summary>Le seul point d'entrée des messages : trouver (ou créer) l'incarnation, déposer dans sa boîte.</summary>
    internal void Deposer(string chemin, Enveloppe enveloppe)
    {
        lock (_verrou)
        {
            if (_quarantaines.TryGetValue(chemin, out var jusquA))
            {
                if (_horloge.GetUtcNow() < jusquA)
                {
                    Refuser(enveloppe, new ActeurIndisponibleException(chemin, jusquA));
                    return;
                }
                _quarantaines.Remove(chemin);
            }

            if (!_activations.TryGetValue(chemin, out var activation))
                activation = Activer(chemin);

            // Boîte non bornée et jamais fermée tant que l'activation est dans le dictionnaire : l'écriture réussit toujours.
            activation.BoiteAuxLettres.Writer.TryWrite(enveloppe);
        }
    }

    // Appelé sous le verrou.
    private Activation Activer(string chemin)
    {
        var separateur = chemin.IndexOf('/');
        var (type, cle) = (chemin[..separateur], chemin[(separateur + 1)..]);
        var activation = new Activation(chemin, _types[type], new ContexteActeur(this, type, cle), _horloge.GetUtcNow());

        // Si une incarnation précédente finit encore de s'arrêter (passivation), la nouvelle l'attend avant de démarrer :
        // jamais deux incarnations du même acteur en même temps, même pendant une fraction de seconde.
        _incarnationsFinissantes.Remove(chemin, out var precedente);
        activation.Boucle = Task.Run(() => Executer(activation, precedente));

        _activations[chemin] = activation;
        Interlocked.Increment(ref _activationsTotales);
        return activation;
    }

    /// <summary>La vie d'une activation : démarrer, puis traiter les messages un par un jusqu'à la fermeture de la boîte.</summary>
    private async Task Executer(Activation activation, Task? precedente)
    {
        if (precedente is not null)
            await precedente;

        if (!await Incarner(activation))
        {
            await Arreter(activation, "son démarrage a échoué");
            return;
        }

        await foreach (var enveloppe in activation.BoiteAuxLettres.Reader.ReadAllAsync())
        {
            activation.EnTraitement = true;
            object? reponse = null;
            Exception? panne = null;
            try
            {
                // LE point clé du modèle : un seul message à la fois, chacun traité jusqu'au bout avant le suivant.
                reponse = await activation.Instance!.Recevoir(enveloppe.Message);
            }
            catch (Exception e)
            {
                panne = e;
            }
            activation.MessagesTraites++;
            activation.DerniereActivite = _horloge.GetUtcNow();

            if (panne is null)
            {
                activation.EnTraitement = false;
                enveloppe.Reponse?.TrySetResult(reponse);  // la comptabilité est à jour quand l'appelant reprend la main
                continue;
            }

            enveloppe.Reponse?.TrySetException(new ActeurEnPanneException(activation.Chemin, panne));
            var continuer = await Superviser(activation, panne);
            activation.EnTraitement = false;
            if (!continuer)
                return;
        }

        // Boîte fermée par la passivation : l'incarnation s'éteint proprement.
        await Desincarner(activation);
    }

    private async Task<bool> Incarner(Activation activation)
    {
        try
        {
            var instance = activation.Type.Fabrique();
            instance.Contexte = activation.Contexte;
            await instance.Demarrer();
            activation.Instance = instance;
            return true;
        }
        catch (Exception e)
        {
            Noter($"{activation.Chemin} : échec du démarrage ({e.Message})");
            return false;
        }
    }

    private async Task Desincarner(Activation activation)
    {
        try
        {
            if (activation.Instance is { } instance)
                await instance.Arreter();
        }
        catch (Exception e)
        {
            Noter($"{activation.Chemin} : erreur à l'arrêt ignorée ({e.Message})");
        }
        activation.Instance = null;
    }

    /// <summary>
    /// Une panne : on ne répare pas, on remplace. L'incarnation fautive (et son état en mémoire, peut-être incohérent)
    /// est jetée ; une neuve recharge l'état durable et reprend les messages SUIVANTS, qui attendaient dans la boîte.
    /// </summary>
    private async Task<bool> Superviser(Activation activation, Exception panne)
    {
        var maintenant = _horloge.GetUtcNow();
        var strategie = activation.Type.Strategie;
        activation.Pannes.RemoveAll(p => maintenant - p > strategie.Fenetre);
        activation.Pannes.Add(maintenant);

        if (activation.Pannes.Count > strategie.MaxRedemarrages)
        {
            await Arreter(activation, $"{activation.Pannes.Count} pannes en {strategie.Fenetre.TotalSeconds:0} s ({panne.Message})");
            return false;
        }

        await Desincarner(activation);
        if (!await Incarner(activation))
        {
            await Arreter(activation, "son redémarrage a échoué");
            return false;
        }
        activation.Redemarrages++;
        Interlocked.Increment(ref _redemarrages);
        Noter($"{activation.Chemin} redémarré après une panne : {panne.Message}");
        return true;
    }

    /// <summary>Arrêt définitif décidé par le superviseur : retrait, quarantaine, et les messages en attente sont refusés.</summary>
    private async Task Arreter(Activation activation, string raison)
    {
        var jusquA = _horloge.GetUtcNow() + activation.Type.Strategie.Fenetre;
        lock (_verrou)
        {
            _activations.Remove(activation.Chemin);
            _incarnationsFinissantes[activation.Chemin] = activation.Boucle!;
            _quarantaines[activation.Chemin] = jusquA;
            activation.BoiteAuxLettres.Writer.TryComplete();
        }
        while (activation.BoiteAuxLettres.Reader.TryRead(out var enAttente))
            Refuser(enAttente, new ActeurIndisponibleException(activation.Chemin, jusquA));

        Interlocked.Increment(ref _arrets);
        Noter($"{activation.Chemin} ARRÊTÉ par son superviseur : {raison} ; quarantaine jusqu'à {jusquA:HH:mm:ss}");
        await Desincarner(activation);
    }

    private void Refuser(Enveloppe enveloppe, Exception raison)
    {
        if (enveloppe.Reponse is { } reponse)
            reponse.TrySetException(raison);
        else
            Interlocked.Increment(ref _lettresMortes);  // un « Tell » que personne ne recevra
    }

    /// <summary>
    /// Endort les acteurs inactifs depuis <paramref name="inactivite"/> : leur mémoire est libérée, leur état durable reste.
    /// Un acteur qui a des messages en attente ou en cours de traitement n'est jamais passivé.
    /// </summary>
    public int Passiver(TimeSpan inactivite)
    {
        var maintenant = _horloge.GetUtcNow();
        List<Activation> endormies;
        lock (_verrou)
        {
            endormies = _activations.Values
                .Where(a => !a.EnTraitement && a.BoiteAuxLettres.Reader.Count == 0 && maintenant - a.DerniereActivite >= inactivite)
                .ToList();
            foreach (var activation in endormies)
            {
                _activations.Remove(activation.Chemin);
                _incarnationsFinissantes[activation.Chemin] = activation.Boucle!;
                // Fermer la boîte : la boucle finit ce qu'elle a déjà pris, puis appelle Arreter().
                activation.BoiteAuxLettres.Writer.TryComplete();
            }
        }
        if (endormies.Count > 0)
        {
            var noms = string.Join(", ", endormies.Take(5).Select(a => a.Chemin));
            Noter($"{endormies.Count} acteur(s) passivé(s) : {noms}{(endormies.Count > 5 ? "…" : "")}");
        }
        Interlocked.Add(ref _passivations, endormies.Count);
        return endormies.Count;
    }

    /// <summary>Passivation périodique, tant que le jeton n'est pas annulé (dans l'API : un service de fond).</summary>
    public async Task PassiverRegulierement(TimeSpan inactivite, TimeSpan periode, CancellationToken arret)
    {
        using var minuterie = new PeriodicTimer(periode, _horloge);
        while (await minuterie.WaitForNextTickAsync(arret))
            Passiver(inactivite);
    }

    public EtatSysteme Etat()
    {
        lock (_verrou)
        {
            var acteurs = _activations.Values.OrderBy(a => a.Chemin)
                .Select(a => new EtatActivation(a.Chemin, a.ActiveLe, a.MessagesTraites, a.BoiteAuxLettres.Reader.Count, a.Redemarrages))
                .ToList();
            return new EtatSysteme(acteurs.Count, Interlocked.Read(ref _activationsTotales), Interlocked.Read(ref _passivations),
                Interlocked.Read(ref _redemarrages), Interlocked.Read(ref _arrets), Interlocked.Read(ref _lettresMortes), acteurs);
        }
    }

    /// <summary>Les derniers événements de supervision, du plus récent au plus ancien.</summary>
    public IReadOnlyList<string> Journal() => _journal.Reverse().ToList();

    private void Noter(string evenement)
    {
        _journal.Enqueue($"{_horloge.GetUtcNow():HH:mm:ss.fff} {evenement}");
        while (_journal.Count > 200)
            _journal.TryDequeue(out _);
    }

    /// <summary>Une incarnation et sa boîte aux lettres.</summary>
    private sealed class Activation(string chemin, TypeActeur type, ContexteActeur contexte, DateTimeOffset maintenant)
    {
        public string Chemin { get; } = chemin;
        public TypeActeur Type { get; } = type;
        public ContexteActeur Contexte { get; } = contexte;
        public DateTimeOffset ActiveLe { get; } = maintenant;

        /// <summary>
        /// Plusieurs écrivains (n'importe quel thread), UN seul lecteur : la boucle de l'acteur. (L'option SingleReader
        /// l'optimiserait, mais un tel canal ne sait plus compter ses messages, ce dont la passivation a besoin.)
        /// </summary>
        public Channel<Enveloppe> BoiteAuxLettres { get; } = Channel.CreateUnbounded<Enveloppe>();

        public Task? Boucle { get; set; }
        public Acteur? Instance { get; set; }
        public volatile bool EnTraitement;
        private long _derniereActivite = maintenant.UtcTicks;

        /// <summary>Écrite par la boucle, lue par la passivation (autre thread) : d'où l'accès atomique.</summary>
        public DateTimeOffset DerniereActivite
        {
            get => new(Interlocked.Read(ref _derniereActivite), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _derniereActivite, value.UtcTicks);
        }

        public long MessagesTraites { get; set; }
        public int Redemarrages { get; set; }
        public List<DateTimeOffset> Pannes { get; } = [];
    }
}
