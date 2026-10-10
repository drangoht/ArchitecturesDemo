using System.Runtime.CompilerServices;
using Mk.Contrats;
using Mk.Noyau;

namespace Mk.Tests;

/// <summary>Le chargement des VRAIES DLL de plugins, compilées dans 13-Microkernel/plugins.</summary>
public class ChargementTests
{
    internal static string DossierPlugins()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "Microkernel.slnx")))
            dossier = dossier.Parent;
        return Path.Combine(dossier!.FullName, "plugins");
    }

    private static string CopieTemporaire()
    {
        var copie = Path.Combine(Path.GetTempPath(), $"plugins-{Guid.NewGuid():N}");
        foreach (var fichier in Directory.GetFiles(DossierPlugins(), "*", SearchOption.AllDirectories))
        {
            var cible = Path.Combine(copie, Path.GetRelativePath(DossierPlugins(), fichier));
            Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
            File.Copy(fichier, cible);
        }
        return copie;
    }

    [Fact]
    public void Les_plugins_sont_decouverts_avec_leurs_points_d_extension_et_leurs_reglages()
    {
        var registre = new RegistrePlugins();

        new ChargeurPlugins(registre, _ => { }).Recharger(DossierPlugins());

        Assert.Equal(["ExportCsv", "ExportQif", "FraisDeVirement", "ListeNoire", "PlafondNocturne"],
            registre.Tous.Select(p => p.Nom).Order());
        Assert.All(registre.Tous, p => Assert.Equal(EtatPlugin.Actif, p.Etat));
        // Le type IControleOperation du plugin EST celui du noyau : les contrats sont bien partagés.
        Assert.Equal(2, registre.Actifs<IControleOperation>().Count());
        Assert.Equal(2, registre.Actifs<IFormatReleve>().Count());
    }

    [Fact]
    public void Les_contrats_ne_sont_charges_qu_une_fois_dans_le_processus()
    {
        new ChargeurPlugins(new RegistrePlugins(), _ => { }).Recharger(DossierPlugins());

        var exemplaires = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Mk.Contrats");
        Assert.Equal(1, exemplaires);
    }

    [Fact]
    public void Un_plugin_supprime_du_dossier_disparait_et_son_contexte_est_decharge()
    {
        var dossier = CopieTemporaire();
        try
        {
            var registre = new RegistrePlugins();
            var chargeur = new ChargeurPlugins(registre, _ => { });
            chargeur.Recharger(dossier);
            Assert.Contains("ListeNoire", Noms(registre));

            // Possible pendant que le plugin est chargé : la DLL a été lue en mémoire, le fichier n'est pas verrouillé.
            Directory.Delete(Path.Combine(dossier, "Mk.Plugins.ListeNoire"), recursive: true);
            var decharges = chargeur.Recharger(dossier);

            Assert.DoesNotContain("ListeNoire", Noms(registre));
            Assert.True(AttendreDechargement(decharges), "Les anciens contextes de chargement n'ont pas été libérés.");
        }
        finally
        {
            Directory.Delete(dossier, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<string> Noms(RegistrePlugins registre) => registre.Tous.Select(p => p.Nom).ToList();

    private static bool AttendreDechargement(IReadOnlyList<WeakReference> contextes)
    {
        for (var i = 0; i < 20 && contextes.Any(c => c.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return contextes.All(c => !c.IsAlive);
    }
}
