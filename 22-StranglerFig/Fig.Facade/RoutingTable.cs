namespace Fig.Facade;

/// <summary>Assembly marker, for the tests (each project of the demo has its own Program).</summary>
public sealed class Marker;

/// <summary>
/// Legacy: the legacy system answers. Shadow: the legacy system answers, the modern one is called in parallel and both responses are
/// compared (reads only). Modern: the modern service answers, the legacy system is no longer called for this route.
/// </summary>
public enum Mode { Legacy, Shadow, Modern }

public sealed class FacadeOptions
{
    public string Legacy { get; set; } = "http://localhost:5240";
    public string Modern { get; set; } = "http://localhost:5241";
    public int ShadowBudgetMs { get; set; } = 500;
    public string AdminKey { get; set; } = "demo";
    public List<RouteDeclaration> Routes { get; set; } = [];
}

public sealed class RouteDeclaration
{
    public string Name { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "";
    public Mode Mode { get; set; }
}

public sealed record RouteState(string Name, string Method, string Path, Mode Mode, long LegacyCalls, long ModernCalls);

public sealed class UnknownRouteException(string name) : Exception($"Unknown route: {name}");

/// <summary>
/// The routing table, modifiable live. It is the one that "strangles" the legacy system: each route switched to
/// Modern mode is a function it no longer performs. It also counts, route by route, who answered.
/// </summary>
public sealed class RoutingTable
{
    public const string Undeclared = "(undeclared)";

    private readonly object _lock = new();
    private readonly List<Route> _routes;
    private readonly Dictionary<string, (long Legacy, long Modern)> _calls = [];
    private long _legacyCallsSinceSwitch;

    public RoutingTable(FacadeOptions options)
    {
        _routes = options.Routes.Select(d => new Route(d.Name, d.Method.ToUpperInvariant(), d.Path, d.Mode)).ToList();
        foreach (var r in _routes)
            Check(r, r.Mode);
    }

    /// <summary>The declared route matching this request, or null: it will then go to the legacy system.</summary>
    public Route? Find(string method, string path)
    {
        lock (_lock)
            return _routes.FirstOrDefault(r => r.Method == method && r.Matches(path));
    }

    public Mode Switch(string name, Mode mode)
    {
        lock (_lock)
        {
            var route = _routes.FirstOrDefault(r => r.Name == name) ?? throw new UnknownRouteException(name);
            Check(route, mode);
            var before = route.Mode;
            route.Mode = mode;
            _legacyCallsSinceSwitch = 0;   // start measuring again what still calls the legacy system
            return before;
        }
    }

    public void Count(string route, bool byLegacy)
    {
        lock (_lock)
        {
            var (legacy, modern) = _calls.GetValueOrDefault(route);
            _calls[route] = byLegacy ? (legacy + 1, modern) : (legacy, modern + 1);
            if (byLegacy)
                _legacyCallsSinceSwitch++;
        }
    }

    public IReadOnlyList<RouteState> State()
    {
        lock (_lock)
            return _routes.Select(r =>
            {
                var (legacy, modern) = _calls.GetValueOrDefault(r.Name);
                return new RouteState(r.Name, r.Method, r.Path, r.Mode, legacy, modern);
            }).ToList();
    }

    public object Progress()
    {
        lock (_lock)
        {
            var migrated = _routes.Count(r => r.Mode == Mode.Modern);
            return new
            {
                declaredRoutes = _routes.Count,
                migratedRoutes = migrated,
                percentage = _routes.Count == 0 ? 0 : 100 * migrated / _routes.Count,
                undeclaredCalls = _calls.GetValueOrDefault(Undeclared).Legacy,
                legacyCallsSinceLastSwitch = _legacyCallsSinceSwitch,
                // Switching off the legacy system requires two things: all the known routes migrated, AND no call still
                // reaching it (a forgotten route, a client calling a function nobody listed…).
                legacyRemovable = migrated == _routes.Count && _legacyCallsSinceSwitch == 0
            };
        }
    }

    // A write has no "shadow" mode: executing it on both sides would make two transfers.
    private static void Check(Route route, Mode mode)
    {
        if (mode == Mode.Shadow && route.Method != "GET")
            throw new ArgumentException($"Shadow mode impossible for {route.Method} {route.Path}: only a read can be executed twice.");
    }

    public sealed class Route(string name, string method, string path, Mode mode)
    {
        private readonly string[] _segments = path.Trim('/').Split('/');

        public string Name { get; } = name;
        public string Method { get; } = method;
        public string Path { get; } = path;
        public Mode Mode { get; internal set; } = mode;

        /// <summary>"/accounts/{number}/operations" matches "/accounts/10001/operations".</summary>
        public bool Matches(string path)
        {
            var segments = path.Trim('/').Split('/');
            return segments.Length == _segments.Length
                && _segments.Zip(segments).All(p => p.First.StartsWith('{') || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));
        }
    }
}
