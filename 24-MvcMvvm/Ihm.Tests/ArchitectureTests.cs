using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ihm.Banque;
using Ihm.Presentation;

namespace Ihm.Tests;

public class ArchitectureTests
{
    private static readonly string[] Ecrans = ["Microsoft.AspNetCore", "PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml"];

    private static string[] References(Assembly assemblage) =>
        assemblage.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("Ihm.") || Ecrans.Any(e => n.StartsWith(e))).Order().ToArray();

    [Fact]
    public void Le_modele_ignore_les_ecrans()
    {
        Assert.Empty(References(typeof(IBanque).Assembly));
    }

    [Fact]
    public void Les_ViewModels_ignorent_WPF_et_le_web()
    {
        // C'est ce qui les rend testables sans écran, et réutilisables par la vue console.
        Assert.Equal(["Ihm.Banque"], References(typeof(VirementViewModel).Assembly));
    }

    [Fact]
    public void Le_site_MVC_n_utilise_pas_les_ViewModels_MVVM()
    {
        Assert.DoesNotContain("Ihm.Presentation", References(typeof(Ihm.Mvc.Marqueur).Assembly));
    }

    // ─── Les liaisons du XAML ───

    private static string Fichier(string relatif, [CallerFilePath] string ici = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ici)!, "..", relatif));

    [Fact]
    public void Toutes_les_liaisons_de_la_fenetre_WPF_visent_des_proprietes_qui_existent()
    {
        var xaml = XDocument.Load(Fichier(Path.Combine("Ihm.Wpf", "MainWindow.xaml")));

        var (liaisons, erreurs) = VerificateurDeLiaisons.Verifier(xaml);

        Assert.Empty(erreurs);
        Assert.True(liaisons >= 25, $"{liaisons} liaisons vérifiées seulement : le vérificateur a-t-il bien parcouru la fenêtre ?");
    }

    [Fact]
    public void Le_verificateur_trouve_les_fautes_de_frappe_que_WPF_laisserait_passer()
    {
        // Sans ce test, WPF afficherait une zone vide et écrirait l'erreur dans la fenêtre de sortie du débogueur.
        var xaml = XDocument.Parse("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                    xmlns:vm="clr-namespace:Ihm.Presentation;assembly=Ihm.Presentation"
                    d:DataContext="{d:DesignInstance Type=vm:EcranPrincipalViewModel}" Title="{Binding Titre}">
                <StackPanel DataContext="{Binding Virement}" d:DataContext="{d:DesignInstance Type=vm:ComptesViewModel}">
                    <TextBox Text="{Binding MontantSaisie, UpdateSourceTrigger=PropertyChanged}" />
                    <ComboBox ItemsSource="{Binding Comptes}" DisplayMemberPath="Afichage" />
                    <ListBox ItemsSource="{Binding Comptes}">
                        <ListBox.ItemTemplate>
                            <DataTemplate DataType="{x:Type vm:OperationViewModel}" />
                        </ListBox.ItemTemplate>
                    </ListBox>
                </StackPanel>
            </Window>
            """);

        var (_, erreurs) = VerificateurDeLiaisons.Verifier(xaml);

        Assert.Equal(
        [
            "StackPanel : DataContext est un VirementViewModel, mais d:DataContext annonce ComptesViewModel.",
            "TextBox.Text : VirementViewModel n'a pas de propriété « MontantSaisie ».",
            "ComboBox.DisplayMemberPath : CompteViewModel n'a pas de propriété « Afichage ».",
            "DataTemplate : la liste contient des CompteViewModel, mais le modèle est prévu pour OperationViewModel.",
        ], erreurs);
    }

    [Fact]
    public void La_vue_WPF_ne_contient_pas_de_code()
    {
        // Pas de gestionnaire d'événement dans le XAML (Click="…"), et un code-behind réduit à InitializeComponent.
        var xaml = File.ReadAllText(Fichier(Path.Combine("Ihm.Wpf", "MainWindow.xaml")));
        Assert.DoesNotMatch(@"\s(Click|TextChanged|SelectionChanged|Loaded|KeyDown|Checked)=""", xaml);

        var codeBehind = File.ReadAllLines(Fichier(Path.Combine("Ihm.Wpf", "MainWindow.xaml.cs")));
        Assert.Equal(["public MainWindow() => InitializeComponent();"], codeBehind.Select(l => l.Trim()).Where(l => l.EndsWith(';') && !l.StartsWith("namespace")));
    }
}

/// <summary>
/// Vérifie les {Binding …} d'un fichier XAML, ce que le compilateur ne fait pas (contrairement aux liaisons compilées
/// de MAUI, Avalonia ou UWP). Le type lié à chaque endroit vient des indications de conception : d:DataContext sur les
/// éléments, DataType sur les modèles d'éléments. Vérifie aussi que ces indications disent vrai.
/// </summary>
public static class VerificateurDeLiaisons
{
    private static readonly XNamespace Conception = "http://schemas.microsoft.com/expression/blend/2008";

    public static (int Liaisons, List<string> Erreurs) Verifier(XDocument xaml)
    {
        var erreurs = new List<string>();
        var liaisons = 0;
        Parcourir(xaml.Root!, contexte: null, elementsDeLaListe: null);
        return (liaisons, erreurs);

        void Parcourir(XElement element, Type? contexte, Type? elementsDeLaListe)
        {
            var nom = element.Name.LocalName;
            var annonce = Type(element.Attribute(Conception + "DataContext")?.Value);
            var typeDuModele = nom == "DataTemplate" ? Type(element.Attribute("DataType")?.Value) : null;

            if (typeDuModele is not null && elementsDeLaListe is not null && typeDuModele != elementsDeLaListe)
                erreurs.Add($"DataTemplate : la liste contient des {elementsDeLaListe.Name}, mais le modèle est prévu pour {typeDuModele.Name}.");

            // DataContext="{Binding X}" se lit dans le contexte du PARENT ; le reste, dans le nouveau contexte.
            Type? reel = null;
            if (Chemin(element.Attribute("DataContext")?.Value) is { } cheminContexte)
            {
                reel = Suivre(contexte, cheminContexte, $"{nom}.DataContext");
                if (reel is not null && annonce is null)
                    erreurs.Add($"{nom} : DataContext lié sans d:DataContext pour dire son type.");
                else if (reel is not null && reel != annonce)
                    erreurs.Add($"{nom} : DataContext est un {reel.Name}, mais d:DataContext annonce {annonce!.Name}.");
            }
            var ici = reel ?? annonce ?? typeDuModele ?? contexte;

            Type? elementsPourLesEnfants = nom == "DataTemplate" ? null : elementsDeLaListe;
            foreach (var attribut in element.Attributes())
            {
                var propriete = attribut.Name.LocalName;
                if (propriete == "DataContext" || attribut.Name.Namespace == Conception)
                    continue;
                if (Chemin(attribut.Value) is not { } chemin)
                    continue;
                var type = Suivre(ici, chemin, $"{nom}.{propriete}");
                if (propriete == "ItemsSource" && type is not null)
                {
                    elementsPourLesEnfants = TypeDesElements(type);
                    if (element.Attribute("DisplayMemberPath")?.Value is { } affiche && elementsPourLesEnfants is not null)
                        Suivre(elementsPourLesEnfants, affiche, $"{nom}.DisplayMemberPath");
                }
            }

            foreach (var enfant in element.Elements())
                Parcourir(enfant, ici, elementsPourLesEnfants);
        }

        // Suit un chemin « A.B.C » à partir d'un type ; null (et une erreur) si un maillon manque.
        Type? Suivre(Type? depart, string chemin, string ou)
        {
            liaisons++;
            if (depart is null)
            {
                erreurs.Add($"{ou} : liaison « {chemin} » sans type connu (ajouter d:DataContext).");
                return null;
            }
            var type = depart;
            foreach (var maillon in chemin.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var propriete = type.GetProperty(maillon);
                if (propriete is null)
                {
                    erreurs.Add($"{ou} : {type.Name} n'a pas de propriété « {maillon} ».");
                    return null;
                }
                type = propriete.PropertyType;
            }
            return type;
        }
    }

    /// <summary>« {Binding X, Converter=…} » ou « {Binding Path=X} » → « X » ; les liaisons vers un autre élément sont ignorées.</summary>
    private static string? Chemin(string? valeur)
    {
        var correspondance = Regex.Match(valeur ?? "", @"^\{Binding\s*(?:Path=)?([\w.]*)\s*(,(?<reste>.*))?\}$");
        if (!correspondance.Success || correspondance.Groups["reste"].Value.Contains("ElementName") || correspondance.Groups["reste"].Value.Contains("RelativeSource"))
            return null;
        return correspondance.Groups[1].Value;
    }

    /// <summary>« {d:DesignInstance Type=vm:X} » ou « {x:Type vm:X} » → le type X des ViewModels.</summary>
    private static Type? Type(string? valeur)
    {
        var correspondance = Regex.Match(valeur ?? "", @"vm:(\w+)");
        return correspondance.Success
            ? typeof(EcranPrincipalViewModel).Assembly.GetType($"{typeof(EcranPrincipalViewModel).Namespace}.{correspondance.Groups[1].Value}", throwOnError: true)
            : null;
    }

    private static Type? TypeDesElements(Type collection) =>
        collection.GetInterfaces().Append(collection)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
}
