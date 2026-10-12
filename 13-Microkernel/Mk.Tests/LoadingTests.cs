using System.Runtime.CompilerServices;
using Mk.Contracts;
using Mk.Kernel;

namespace Mk.Tests;

/// <summary>The loading of the REAL plugin DLLs, built into 13-Microkernel/plugins.</summary>
public class LoadingTests
{
    internal static string PluginsFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Microkernel.slnx")))
            folder = folder.Parent;
        return Path.Combine(folder!.FullName, "plugins");
    }

    private static string TemporaryCopy()
    {
        var copy = Path.Combine(Path.GetTempPath(), $"plugins-{Guid.NewGuid():N}");
        foreach (var file in Directory.GetFiles(PluginsFolder(), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(copy, Path.GetRelativePath(PluginsFolder(), file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return copy;
    }

    [Fact]
    public void The_plugins_are_discovered_with_their_extension_points_and_their_settings()
    {
        var registry = new PluginRegistry();

        new PluginLoader(registry, _ => { }).Reload(PluginsFolder());

        Assert.Equal(["Blocklist", "CsvExport", "NightLimit", "QifExport", "TransferFees"],
            registry.All.Select(p => p.Name).Order());
        Assert.All(registry.All, p => Assert.Equal(PluginState.Active, p.State));
        // The plugin's IOperationCheck type IS the kernel's: the contracts are really shared.
        Assert.Equal(2, registry.Active<IOperationCheck>().Count());
        Assert.Equal(2, registry.Active<IStatementFormat>().Count());
    }

    [Fact]
    public void The_contracts_are_loaded_only_once_in_the_process()
    {
        new PluginLoader(new PluginRegistry(), _ => { }).Reload(PluginsFolder());

        var copies = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Mk.Contracts");
        Assert.Equal(1, copies);
    }

    [Fact]
    public void A_plugin_deleted_from_the_folder_disappears_and_its_context_is_unloaded()
    {
        var folder = TemporaryCopy();
        try
        {
            var registry = new PluginRegistry();
            var loader = new PluginLoader(registry, _ => { });
            loader.Reload(folder);
            Assert.Contains("Blocklist", Names(registry));

            // Possible while the plugin is loaded: the DLL was read into memory, the file is not locked.
            Directory.Delete(Path.Combine(folder, "Mk.Plugins.Blocklist"), recursive: true);
            var unloaded = loader.Reload(folder);

            Assert.DoesNotContain("Blocklist", Names(registry));
            Assert.True(WaitForUnloading(unloaded), "The old load contexts were not released.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<string> Names(PluginRegistry registry) => registry.All.Select(p => p.Name).ToList();

    private static bool WaitForUnloading(IReadOnlyList<WeakReference> contexts)
    {
        for (var i = 0; i < 20 && contexts.Any(c => c.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return contexts.All(c => !c.IsAlive);
    }
}
