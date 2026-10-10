using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Mk.Contrats;

namespace Mk.Noyau;

/// <summary>
/// Un contexte de chargement par plugin : chaque plugin a ses propres dépendances, et on peut le décharger.
/// </summary>
internal sealed class ContexteChargement(string cheminDll)
    : AssemblyLoadContext(Path.GetFileNameWithoutExtension(cheminDll), isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolveur = new(cheminDll);

    /// <summary>
    /// Chargé depuis la MÉMOIRE et non depuis le fichier : la DLL n'est pas verrouillée,
    /// on peut la recompiler, la remplacer ou la supprimer pendant que l'application tourne.
    /// </summary>
    public Assembly Charger() => LoadFromStream(new MemoryStream(File.ReadAllBytes(cheminDll)));

    protected override Assembly? Load(AssemblyName nom)
    {
        // Point crucial : les contrats doivent venir du NOYAU (contexte par défaut).
        // Si le plugin chargeait sa propre copie de Mk.Contrats, son IPlugin serait un AUTRE type
        // que l'IPlugin du noyau (même nom, assemblies différentes), et rien ne fonctionnerait.
        if (nom.Name == typeof(IPlugin).Assembly.GetName().Name)
            return null;

        var chemin = _resolveur.ResolveAssemblyToPath(nom);
        return chemin is null ? null : LoadFromStream(new MemoryStream(File.ReadAllBytes(chemin)));
    }
}

/// <summary>
/// Découvre les plugins : un sous-dossier par plugin, contenant {NomDuDossier}.dll et, éventuellement, plugin.json.
/// </summary>
public sealed class ChargeurPlugins(RegistrePlugins registre, Action<string> journal)
{
    private readonly List<ContexteChargement> _contextes = [];
    private readonly Lock _verrou = new();

    /// <summary>
    /// Recharge tous les plugins du dossier, puis décharge les anciens.
    /// Renvoie des références faibles vers les contextes déchargés (pour vérifier qu'ils sont bien libérés).
    /// </summary>
    public IReadOnlyList<WeakReference> Recharger(string dossier)
    {
        lock (_verrou)
        {
            var anciens = _contextes.ToList();
            _contextes.Clear();

            var plugins = new List<PluginCharge>();
            foreach (var sousDossier in Directory.Exists(dossier) ? Directory.GetDirectories(dossier) : [])
                plugins.AddRange(ChargerDossier(sousDossier));

            registre.Publier(plugins);
            journal($"{plugins.Count} plugin(s) chargé(s) depuis {dossier} : {string.Join(", ", plugins.Select(p => $"{p.Nom} ({p.Etat})"))}");

            return anciens.Select(contexte =>
            {
                var reference = new WeakReference(contexte);
                contexte.Unload();
                return reference;
            }).ToList();
        }
    }

    private IEnumerable<PluginCharge> ChargerDossier(string sousDossier)
    {
        var nom = Path.GetFileName(sousDossier);
        var dll = Path.Combine(sousDossier, $"{nom}.dll");
        if (!File.Exists(dll))
            return [];

        try
        {
            var contexte = new ContexteChargement(dll);
            _contextes.Add(contexte);
            var assembly = contexte.Charger();
            var reglages = LireReglages(Path.Combine(sousDossier, "plugin.json"));

            return assembly.GetExportedTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(t))
                .Select(t => RegistrePlugins.Preparer((IPlugin)Activator.CreateInstance(t)!, nom, reglages, journal))
                .ToList();
        }
        catch (Exception e)
        {
            journal($"{nom} : chargement impossible ({e.Message})");
            return [];
        }
    }

    private static Dictionary<string, string> LireReglages(string fichier) =>
        File.Exists(fichier)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fichier)) ?? []
            : [];
}
