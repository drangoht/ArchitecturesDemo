namespace Fig.Facade;

/// <summary>Repère de l'assemblage, pour les tests (chaque projet de la démo a son propre Program).</summary>
public sealed class Marqueur;

/// <summary>
/// Ancien : l'ancien répond. Ombre : l'ancien répond, le nouveau est appelé en parallèle et les deux réponses sont
/// comparées (lectures seulement). Nouveau : le nouveau répond, l'ancien n'est plus appelé pour cette route.
/// </summary>
public enum Mode { Ancien, Ombre, Nouveau }

public sealed class OptionsFacade
{
    public string Ancien { get; set; } = "http://localhost:5240";
    public string Nouveau { get; set; } = "http://localhost:5241";
    public int BudgetOmbreMs { get; set; } = 500;
    public string CleAdmin { get; set; } = "demo";
    public List<DeclarationRoute> Routes { get; set; } = [];
}

public sealed class DeclarationRoute
{
    public string Nom { get; set; } = "";
    public string Methode { get; set; } = "GET";
    public string Chemin { get; set; } = "";
    public Mode Mode { get; set; }
}

public sealed record EtatRoute(string Nom, string Methode, string Chemin, Mode Mode, long AppelsAncien, long AppelsNouveau);

public sealed class RouteInconnueException(string nom) : Exception($"Route inconnue : {nom}");

/// <summary>
/// La table de routage, modifiable à chaud. C'est elle qui « étrangle » l'ancien : chaque route basculée en mode
/// Nouveau est une fonction qu'il ne rend plus. Elle compte aussi, route par route, qui a répondu.
/// </summary>
public sealed class TableDeRoutage
{
    public const string NonDeclaree = "(non déclarée)";

    private readonly object _verrou = new();
    private readonly List<Route> _routes;
    private readonly Dictionary<string, (long Ancien, long Nouveau)> _appels = [];
    private long _appelsAncienDepuisBascule;

    public TableDeRoutage(OptionsFacade options)
    {
        _routes = options.Routes.Select(d => new Route(d.Nom, d.Methode.ToUpperInvariant(), d.Chemin, d.Mode)).ToList();
        foreach (var r in _routes)
            Verifier(r, r.Mode);
    }

    /// <summary>La route déclarée qui correspond à cette requête, ou null : elle ira alors à l'ancien.</summary>
    public Route? Trouver(string methode, string chemin)
    {
        lock (_verrou)
            return _routes.FirstOrDefault(r => r.Methode == methode && r.Correspond(chemin));
    }

    public Mode Basculer(string nom, Mode mode)
    {
        lock (_verrou)
        {
            var route = _routes.FirstOrDefault(r => r.Nom == nom) ?? throw new RouteInconnueException(nom);
            Verifier(route, mode);
            var avant = route.Mode;
            route.Mode = mode;
            _appelsAncienDepuisBascule = 0;   // on recommence à mesurer ce qui appelle encore l'ancien
            return avant;
        }
    }

    public void Compter(string route, bool parLAncien)
    {
        lock (_verrou)
        {
            var (ancien, nouveau) = _appels.GetValueOrDefault(route);
            _appels[route] = parLAncien ? (ancien + 1, nouveau) : (ancien, nouveau + 1);
            if (parLAncien)
                _appelsAncienDepuisBascule++;
        }
    }

    public IReadOnlyList<EtatRoute> Etat()
    {
        lock (_verrou)
            return _routes.Select(r =>
            {
                var (ancien, nouveau) = _appels.GetValueOrDefault(r.Nom);
                return new EtatRoute(r.Nom, r.Methode, r.Chemin, r.Mode, ancien, nouveau);
            }).ToList();
    }

    public object Progression()
    {
        lock (_verrou)
        {
            var migrees = _routes.Count(r => r.Mode == Mode.Nouveau);
            return new
            {
                routesDeclarees = _routes.Count,
                routesMigrees = migrees,
                pourcentage = _routes.Count == 0 ? 0 : 100 * migrees / _routes.Count,
                appelsNonDeclares = _appels.GetValueOrDefault(NonDeclaree).Ancien,
                appelsAncienDepuisDerniereBascule = _appelsAncienDepuisBascule,
                // Éteindre l'ancien exige deux choses : toutes les routes connues migrées, ET plus aucun appel qui lui
                // parvienne encore (une route oubliée, un client qui appelle une fonction que personne n'a recensée…).
                ancienRetirable = migrees == _routes.Count && _appelsAncienDepuisBascule == 0
            };
        }
    }

    // Une écriture n'a pas de mode « ombre » : l'exécuter des deux côtés ferait deux virements.
    private static void Verifier(Route route, Mode mode)
    {
        if (mode == Mode.Ombre && route.Methode != "GET")
            throw new ArgumentException($"Mode Ombre impossible pour {route.Methode} {route.Chemin} : seule une lecture peut être exécutée deux fois.");
    }

    public sealed class Route(string nom, string methode, string chemin, Mode mode)
    {
        private readonly string[] _segments = chemin.Trim('/').Split('/');

        public string Nom { get; } = nom;
        public string Methode { get; } = methode;
        public string Chemin { get; } = chemin;
        public Mode Mode { get; internal set; } = mode;

        /// <summary>« /comptes/{numero}/operations » correspond à « /comptes/10001/operations ».</summary>
        public bool Correspond(string chemin)
        {
            var segments = chemin.Trim('/').Split('/');
            return segments.Length == _segments.Length
                && _segments.Zip(segments).All(p => p.First.StartsWith('{') || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));
        }
    }
}
