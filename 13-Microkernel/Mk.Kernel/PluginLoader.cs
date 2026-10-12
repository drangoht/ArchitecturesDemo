using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Mk.Contracts;

namespace Mk.Kernel;

/// <summary>
/// One load context per plugin: each plugin has its own dependencies, and it can be unloaded.
/// </summary>
internal sealed class PluginLoadContext(string dllPath)
    : AssemblyLoadContext(Path.GetFileNameWithoutExtension(dllPath), isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(dllPath);

    /// <summary>
    /// Loaded from MEMORY and not from the file: the DLL is not locked,
    /// it can be rebuilt, replaced or deleted while the application is running.
    /// </summary>
    public Assembly LoadPlugin() => LoadFromStream(new MemoryStream(File.ReadAllBytes(dllPath)));

    protected override Assembly? Load(AssemblyName name)
    {
        // Crucial point: the contracts must come from the KERNEL (default context).
        // If the plugin loaded its own copy of Mk.Contracts, its IPlugin would be ANOTHER type
        // than the kernel's IPlugin (same name, different assemblies), and nothing would work.
        if (name.Name == typeof(IPlugin).Assembly.GetName().Name)
            return null;

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
    }
}

/// <summary>
/// Discovers the plugins: one subfolder per plugin, containing {FolderName}.dll and, optionally, plugin.json.
/// </summary>
public sealed class PluginLoader(PluginRegistry registry, Action<string> log)
{
    private readonly List<PluginLoadContext> _contexts = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Reloads all the plugins of the folder, then unloads the old ones.
    /// Returns weak references to the unloaded contexts (to check that they are really released).
    /// </summary>
    public IReadOnlyList<WeakReference> Reload(string folder)
    {
        lock (_lock)
        {
            var old = _contexts.ToList();
            _contexts.Clear();

            var plugins = new List<LoadedPlugin>();
            foreach (var subfolder in Directory.Exists(folder) ? Directory.GetDirectories(folder) : [])
                plugins.AddRange(LoadFolder(subfolder));

            registry.Publish(plugins);
            log($"{plugins.Count} plugin(s) loaded from {folder}: {string.Join(", ", plugins.Select(p => $"{p.Name} ({p.State})"))}");

            return old.Select(context =>
            {
                var reference = new WeakReference(context);
                context.Unload();
                return reference;
            }).ToList();
        }
    }

    private IEnumerable<LoadedPlugin> LoadFolder(string subfolder)
    {
        var name = Path.GetFileName(subfolder);
        var dll = Path.Combine(subfolder, $"{name}.dll");
        if (!File.Exists(dll))
            return [];

        try
        {
            var context = new PluginLoadContext(dll);
            _contexts.Add(context);
            var assembly = context.LoadPlugin();
            var settings = ReadSettings(Path.Combine(subfolder, "plugin.json"));

            return assembly.GetExportedTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(t))
                .Select(t => PluginRegistry.Prepare((IPlugin)Activator.CreateInstance(t)!, name, settings, log))
                .ToList();
        }
        catch (Exception e)
        {
            log($"{name}: cannot be loaded ({e.Message})");
            return [];
        }
    }

    private static Dictionary<string, string> ReadSettings(string file) =>
        File.Exists(file)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? []
            : [];
}
