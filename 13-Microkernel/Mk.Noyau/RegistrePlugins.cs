using System.Collections.Immutable;
using Mk.Contrats;

namespace Mk.Noyau;

public enum EtatPlugin { Actif, Desactive, Incompatible, EnErreur }

public sealed class PluginCharge(IPlugin instance, string source)
{
    public IPlugin Instance { get; } = instance;
    public string Source { get; } = source;
    public string Nom => Instance.Nom;
    public EtatPlugin Etat { get; internal set; } = EtatPlugin.Actif;
    public string? Erreur { get; internal set; }

    public IEnumerable<string> PointsExtension => Instance.GetType().GetInterfaces()
        .Where(i => i != typeof(IPlugin) && typeof(IPlugin).IsAssignableFrom(i))
        .Select(i => i.Name);
}

/// <summary>
/// Les plugins actuellement connus du noyau. La liste est remplacée d'un bloc à chaque rechargement :
/// une opération en cours garde la liste qu'elle a commencé à utiliser.
/// </summary>
public sealed class RegistrePlugins
{
    public static readonly Version VersionNoyau = new(1, 2);

    private ImmutableList<PluginCharge> _plugins = [];

    public IReadOnlyList<PluginCharge> Tous => _plugins;

    public IEnumerable<T> Actifs<T>() where T : IPlugin =>
        _plugins.Where(p => p.Etat == EtatPlugin.Actif).Select(p => p.Instance).OfType<T>();

    /// <summary>Vérifie la compatibilité et initialise un plugin. Une erreur ne l'empêche pas d'être listé (avec son état).</summary>
    public static PluginCharge Preparer(IPlugin plugin, string source, IReadOnlyDictionary<string, string> reglages, Action<string> journal)
    {
        var charge = new PluginCharge(plugin, source);
        if (plugin.VersionNoyauMinimale > VersionNoyau)
        {
            charge.Etat = EtatPlugin.Incompatible;
            charge.Erreur = $"Exige le noyau {plugin.VersionNoyauMinimale}, version actuelle {VersionNoyau}.";
            return charge;
        }
        try
        {
            plugin.Initialiser(new ContextePlugin(reglages, message => journal($"[{plugin.Nom}] {message}")));
        }
        catch (Exception e)
        {
            (charge.Etat, charge.Erreur) = (EtatPlugin.EnErreur, $"Initialisation impossible : {e.Message}");
        }
        return charge;
    }

    public void Publier(IEnumerable<PluginCharge> plugins) => Interlocked.Exchange(ref _plugins, plugins.ToImmutableList());

    /// <summary>Ajoute un plugin déjà instancié (sans DLL) : pratique pour les tests.</summary>
    public PluginCharge Enregistrer(IPlugin plugin, IReadOnlyDictionary<string, string>? reglages = null)
    {
        var charge = Preparer(plugin, "mémoire", reglages ?? new Dictionary<string, string>(), _ => { });
        ImmutableInterlocked.Update(ref _plugins, liste => liste.Add(charge));
        return charge;
    }

    public bool ChangerEtat(string nom, bool actif)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase));
        if (plugin is null || plugin.Etat is EtatPlugin.Incompatible)
            return false;
        (plugin.Etat, plugin.Erreur) = (actif ? EtatPlugin.Actif : EtatPlugin.Desactive, null);
        return true;
    }

    internal void SignalerErreur(IPlugin plugin, Exception erreur)
    {
        var charge = _plugins.FirstOrDefault(p => ReferenceEquals(p.Instance, plugin));
        if (charge is not null)
            (charge.Etat, charge.Erreur) = (EtatPlugin.EnErreur, erreur.Message);
    }
}
