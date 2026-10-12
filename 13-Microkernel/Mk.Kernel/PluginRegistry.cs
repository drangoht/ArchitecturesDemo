using System.Collections.Immutable;
using Mk.Contracts;

namespace Mk.Kernel;

public enum PluginState { Active, Disabled, Incompatible, Faulted }

public sealed class LoadedPlugin(IPlugin instance, string source)
{
    public IPlugin Instance { get; } = instance;
    public string Source { get; } = source;
    public string Name => Instance.Name;
    public PluginState State { get; internal set; } = PluginState.Active;
    public string? Error { get; internal set; }

    public IEnumerable<string> ExtensionPoints => Instance.GetType().GetInterfaces()
        .Where(i => i != typeof(IPlugin) && typeof(IPlugin).IsAssignableFrom(i))
        .Select(i => i.Name);
}

/// <summary>
/// The plugins currently known to the kernel. The list is replaced in one go at each reload:
/// an operation in progress keeps the list it started using.
/// </summary>
public sealed class PluginRegistry
{
    public static readonly Version KernelVersion = new(1, 2);

    private ImmutableList<LoadedPlugin> _plugins = [];

    public IReadOnlyList<LoadedPlugin> All => _plugins;

    public IEnumerable<T> Active<T>() where T : IPlugin =>
        _plugins.Where(p => p.State == PluginState.Active).Select(p => p.Instance).OfType<T>();

    /// <summary>Checks the compatibility and initializes a plugin. An error does not prevent it from being listed (with its state).</summary>
    public static LoadedPlugin Prepare(IPlugin plugin, string source, IReadOnlyDictionary<string, string> settings, Action<string> log)
    {
        var loaded = new LoadedPlugin(plugin, source);
        if (plugin.MinimumKernelVersion > KernelVersion)
        {
            loaded.State = PluginState.Incompatible;
            loaded.Error = $"Requires kernel {plugin.MinimumKernelVersion}, current version {KernelVersion}.";
            return loaded;
        }
        try
        {
            plugin.Initialize(new PluginContext(settings, message => log($"[{plugin.Name}] {message}")));
        }
        catch (Exception e)
        {
            (loaded.State, loaded.Error) = (PluginState.Faulted, $"Initialization impossible: {e.Message}");
        }
        return loaded;
    }

    public void Publish(IEnumerable<LoadedPlugin> plugins) => Interlocked.Exchange(ref _plugins, plugins.ToImmutableList());

    /// <summary>Adds an already instantiated plugin (without a DLL): handy for the tests.</summary>
    public LoadedPlugin Register(IPlugin plugin, IReadOnlyDictionary<string, string>? settings = null)
    {
        var loaded = Prepare(plugin, "memory", settings ?? new Dictionary<string, string>(), _ => { });
        ImmutableInterlocked.Update(ref _plugins, list => list.Add(loaded));
        return loaded;
    }

    public bool ChangeState(string name, bool active)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (plugin is null || plugin.State is PluginState.Incompatible)
            return false;
        (plugin.State, plugin.Error) = (active ? PluginState.Active : PluginState.Disabled, null);
        return true;
    }

    internal void ReportError(IPlugin plugin, Exception error)
    {
        var loaded = _plugins.FirstOrDefault(p => ReferenceEquals(p.Instance, plugin));
        if (loaded is not null)
            (loaded.State, loaded.Error) = (PluginState.Faulted, error.Message);
    }
}
