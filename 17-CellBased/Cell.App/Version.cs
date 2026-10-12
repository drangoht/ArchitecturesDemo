namespace Cell.App;

/// <summary>
/// The version of the code running in THIS cell. A real deployment would replace the binaries; here, the control plane
/// changes the version through PUT /admin/version and the behaviour follows. What matters: each cell can run
/// a different version, while a deployment progresses cell by cell.
/// </summary>
public sealed class CellVersion(string initial)
{
    public static readonly string[] Known = ["1.0", "1.1", "1.2"];

    private readonly List<string> _history = [initial];

    public string Current
    {
        get { lock (_history) return _history[^1]; }
        set { lock (_history) _history.Add(value); }
    }

    /// <summary>All the versions that ran here, rollbacks included: shows how far a deployment went.</summary>
    public IReadOnlyList<string> History
    {
        get { lock (_history) return [.. _history]; }
    }

    /// <summary>
    /// The computation of a withdrawal. The 1.1 contains a bug (the amount is debited twice): it does not show at compile time,
    /// only at run time. The 1.2 fixes it.
    /// </summary>
    public decimal Withdraw(decimal balance, decimal amount) => Current == "1.1" ? balance - 2 * amount : balance - amount;

    /// <summary>
    /// The health probe replays a withdrawal on a dummy account and checks the result. It is what lets the deployment
    /// stop a faulty version at the first cell, before it reaches all the customers.
    /// </summary>
    public (bool Healthy, string Detail) Probe()
    {
        var result = Withdraw(100m, 10m);
        return result == 90m
            ? (true, "check withdrawal correct")
            : (false, $"check withdrawal incorrect: 100 - 10 = {result}");
    }
}

/// <summary>Simulated failure of the whole cell (machine lost, corrupted database, bad configuration…).</summary>
public sealed class CellState
{
    public bool Down { get; set; }
}
