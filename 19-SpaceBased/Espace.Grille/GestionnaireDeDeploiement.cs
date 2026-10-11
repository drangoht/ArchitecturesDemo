namespace Espace.Grille;

public sealed class DemarrageAFroidImpossibleException(int enRetard)
    : Exception($"Démarrage à froid impossible pour l'instant : la base a encore {enRetard} écritures de retard.");

/// <summary>
/// Le gestionnaire de déploiement : il démarre et arrête les unités de traitement selon la charge (élasticité).
/// Une nouvelle unité reçoit une copie de la grille depuis une unité existante ; s'il n'en reste aucune, depuis la base
/// (démarrage à froid, par le lecteur de données).
/// </summary>
public sealed class GestionnaireDeDeploiement
{
    private readonly Plateforme _plateforme;
    private readonly Func<object, ContexteUnite, Task<object>> _traitement;
    private readonly SemaphoreSlim _unSeulDemarrageAFroid = new(1);
    private int _numero, _demarragesAFroid;

    internal GestionnaireDeDeploiement(Plateforme plateforme, Func<object, ContexteUnite, Task<object>> traitement)
    {
        _plateforme = plateforme;
        _traitement = traitement;
        plateforme.Messages.DemarrageAFroid = DemarrerAFroid;
    }

    public int DemarragesAFroid => Volatile.Read(ref _demarragesAFroid);

    public async Task<UniteDeTraitement> Demarrer()
    {
        var id = $"u{Interlocked.Increment(ref _numero)}";
        var source = _plateforme.Messages.Unites().FirstOrDefault();
        Replique replique;
        if (source is not null)
        {
            replique = _plateforme.Donnees.CreerReplique(id, source.Replique.Toutes);
            _plateforme.Noter($"{id} démarrée : grille copiée depuis {source.Id} ({replique.Nombre} entrées)");
        }
        else
        {
            // LECTEUR DE DONNÉES. Attendre d'abord que la pompe ait tout écrit : sinon on chargerait des valeurs périmées,
            // et les écritures en retard viendraient ensuite écraser dans la base celles de la nouvelle unité.
            // Base en panne : impossible de démarrer à froid, la requête échoue (503) au lieu d'attendre indéfiniment.
            if (!_plateforme.Base.Disponible)
                throw new BaseIndisponibleException();
            try
            {
                using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _plateforme.Pompe.AttendreVidage(delai.Token);
            }
            catch (OperationCanceledException)
            {
                throw new DemarrageAFroidImpossibleException(_plateforme.Pompe.EnAttente);
            }
            replique = _plateforme.Donnees.CreerReplique(id, _plateforme.Base.Tout);
            Interlocked.Increment(ref _demarragesAFroid);
            _plateforme.Noter($"{id} démarrée À FROID : grille chargée depuis la base ({replique.Nombre} entrées)");
        }

        var unite = new UniteDeTraitement(id, _plateforme.Options.CapaciteParUnite, replique, _traitement,
            _plateforme.Simulation, _plateforme.Messages);
        _plateforme.Messages.Ajouter(unite);
        return unite;
    }

    /// <summary>Plus aucune unité : la première requête en fait démarrer une (une seule, même si mille requêtes arrivent).</summary>
    private async Task DemarrerAFroid()
    {
        await _unSeulDemarrageAFroid.WaitAsync();
        try
        {
            if (_plateforme.Messages.Unites().Count == 0)
                await Demarrer();
        }
        finally
        {
            _unSeulDemarrageAFroid.Release();
        }
    }

    /// <summary>Arrêt propre : l'unité ne reçoit plus rien, finit ses requêtes, puis disparaît. Les données restent dans les autres répliques.</summary>
    public async Task Arreter(UniteDeTraitement unite)
    {
        _plateforme.Messages.Retirer(unite);
        await unite.Vider();
        _plateforme.Donnees.RetirerReplique(unite.Id);
        _plateforme.Noter($"{unite.Id} arrêtée ({unite.Traitees} requêtes traitées)");
    }

    /// <summary>Panne brutale : l'unité et sa mémoire disparaissent d'un coup.</summary>
    public void Tuer(UniteDeTraitement unite)
    {
        unite.Tuer();
        _plateforme.Messages.Retirer(unite);
        _plateforme.Donnees.RetirerReplique(unite.Id);
        _plateforme.Noter($"{unite.Id} EN PANNE : sa réplique est perdue");
    }

    /// <summary>
    /// Une évaluation : trop chargé → une unité de plus ; presque vide → une de moins. Appelée régulièrement (service de fond).
    /// </summary>
    public async Task<string?> Evaluer()
    {
        var unites = _plateforme.Messages.Unites();
        if (unites.Count == 0)
            return null;
        var options = _plateforme.Options;
        var charge = unites.Average(u => u.Charge);

        if (charge >= options.SeuilHaut && unites.Count < options.UnitesMax)
        {
            var unite = await Demarrer();
            return $"charge {charge:P0} : {unite.Id} ajoutée";
        }
        if (charge <= options.SeuilBas && unites.Count > options.UnitesMin)
        {
            var moinsChargee = unites.MinBy(u => u.Charge)!;
            await Arreter(moinsChargee);
            return $"charge {charge:P0} : {moinsChargee.Id} retirée";
        }
        return null;
    }
}
