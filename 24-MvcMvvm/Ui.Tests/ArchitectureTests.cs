using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ui.Bank;
using Ui.Presentation;

namespace Ui.Tests;

public class ArchitectureTests
{
    private static readonly string[] Screens = ["Microsoft.AspNetCore", "PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml"];

    private static string[] References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("Ui.") || Screens.Any(e => n.StartsWith(e))).Order().ToArray();

    [Fact]
    public void The_model_knows_nothing_about_the_screens()
    {
        Assert.Empty(References(typeof(IBank).Assembly));
    }

    [Fact]
    public void The_ViewModels_know_nothing_about_WPF_and_the_web()
    {
        // That is what makes them testable without a screen, and reusable by the console view.
        Assert.Equal(["Ui.Bank"], References(typeof(TransferViewModel).Assembly));
    }

    [Fact]
    public void The_MVC_site_does_not_use_the_MVVM_ViewModels()
    {
        Assert.DoesNotContain("Ui.Presentation", References(typeof(Ui.Mvc.Marker).Assembly));
    }

    // ─── The bindings of the XAML ───

    private static string FilePath(string relative, [CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", relative));

    [Fact]
    public void All_the_bindings_of_the_WPF_window_target_properties_that_exist()
    {
        var xaml = XDocument.Load(FilePath(Path.Combine("Ui.Wpf", "MainWindow.xaml")));

        var (bindings, errors) = BindingChecker.Check(xaml);

        Assert.Empty(errors);
        Assert.True(bindings >= 25, $"Only {bindings} bindings checked: did the checker really go through the window?");
    }

    [Fact]
    public void The_checker_finds_the_typos_that_WPF_would_let_through()
    {
        // Without this test, WPF would display an empty area and write the error in the debugger's output window.
        var xaml = XDocument.Parse("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                    xmlns:vm="clr-namespace:Ui.Presentation;assembly=Ui.Presentation"
                    d:DataContext="{d:DesignInstance Type=vm:MainScreenViewModel}" Title="{Binding Title}">
                <StackPanel DataContext="{Binding Transfer}" d:DataContext="{d:DesignInstance Type=vm:AccountsViewModel}">
                    <TextBox Text="{Binding AmountTex, UpdateSourceTrigger=PropertyChanged}" />
                    <ComboBox ItemsSource="{Binding Accounts}" DisplayMemberPath="Dispaly" />
                    <ListBox ItemsSource="{Binding Accounts}">
                        <ListBox.ItemTemplate>
                            <DataTemplate DataType="{x:Type vm:OperationViewModel}" />
                        </ListBox.ItemTemplate>
                    </ListBox>
                </StackPanel>
            </Window>
            """);

        var (_, errors) = BindingChecker.Check(xaml);

        Assert.Equal(
        [
            "StackPanel: DataContext is a TransferViewModel, but d:DataContext announces AccountsViewModel.",
            "TextBox.Text: TransferViewModel has no property \"AmountTex\".",
            "ComboBox.DisplayMemberPath: AccountViewModel has no property \"Dispaly\".",
            "DataTemplate: the list contains AccountViewModel items, but the template is meant for OperationViewModel.",
        ], errors);
    }

    [Fact]
    public void The_WPF_view_contains_no_code()
    {
        // No event handler in the XAML (Click="…"), and a code-behind reduced to InitializeComponent.
        var xaml = File.ReadAllText(FilePath(Path.Combine("Ui.Wpf", "MainWindow.xaml")));
        Assert.DoesNotMatch(@"\s(Click|TextChanged|SelectionChanged|Loaded|KeyDown|Checked)=""", xaml);

        var codeBehind = File.ReadAllLines(FilePath(Path.Combine("Ui.Wpf", "MainWindow.xaml.cs")));
        Assert.Equal(["public MainWindow() => InitializeComponent();"], codeBehind.Select(l => l.Trim()).Where(l => l.EndsWith(';') && !l.StartsWith("namespace")));
    }
}

/// <summary>
/// Checks the {Binding …} of a XAML file, which the compiler does not do (unlike the compiled bindings
/// of MAUI, Avalonia or UWP). The type bound at each place comes from the design-time hints: d:DataContext on the
/// elements, DataType on the item templates. Also checks that these hints tell the truth.
/// </summary>
public static class BindingChecker
{
    private static readonly XNamespace Design = "http://schemas.microsoft.com/expression/blend/2008";

    public static (int Bindings, List<string> Errors) Check(XDocument xaml)
    {
        var errors = new List<string>();
        var bindings = 0;
        Walk(xaml.Root!, context: null, listItems: null);
        return (bindings, errors);

        void Walk(XElement element, Type? context, Type? listItems)
        {
            var name = element.Name.LocalName;
            var announced = Type(element.Attribute(Design + "DataContext")?.Value);
            var templateType = name == "DataTemplate" ? Type(element.Attribute("DataType")?.Value) : null;

            if (templateType is not null && listItems is not null && templateType != listItems)
                errors.Add($"DataTemplate: the list contains {listItems.Name} items, but the template is meant for {templateType.Name}.");

            // DataContext="{Binding X}" is read in the PARENT's context; the rest, in the new context.
            Type? actual = null;
            if (Path(element.Attribute("DataContext")?.Value) is { } contextPath)
            {
                actual = Follow(context, contextPath, $"{name}.DataContext");
                if (actual is not null && announced is null)
                    errors.Add($"{name}: DataContext bound without d:DataContext to tell its type.");
                else if (actual is not null && actual != announced)
                    errors.Add($"{name}: DataContext is a {actual.Name}, but d:DataContext announces {announced!.Name}.");
            }
            var here = actual ?? announced ?? templateType ?? context;

            Type? itemsForChildren = name == "DataTemplate" ? null : listItems;
            foreach (var attribute in element.Attributes())
            {
                var property = attribute.Name.LocalName;
                if (property == "DataContext" || attribute.Name.Namespace == Design)
                    continue;
                if (Path(attribute.Value) is not { } path)
                    continue;
                var type = Follow(here, path, $"{name}.{property}");
                if (property == "ItemsSource" && type is not null)
                {
                    itemsForChildren = ItemType(type);
                    if (element.Attribute("DisplayMemberPath")?.Value is { } display && itemsForChildren is not null)
                        Follow(itemsForChildren, display, $"{name}.DisplayMemberPath");
                }
            }

            foreach (var child in element.Elements())
                Walk(child, here, itemsForChildren);
        }

        // Follows a path "A.B.C" from a type; null (and an error) if a link is missing.
        Type? Follow(Type? start, string path, string where)
        {
            bindings++;
            if (start is null)
            {
                errors.Add($"{where}: binding \"{path}\" without known type (add d:DataContext).");
                return null;
            }
            var type = start;
            foreach (var link in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var property = type.GetProperty(link);
                if (property is null)
                {
                    errors.Add($"{where}: {type.Name} has no property \"{link}\".");
                    return null;
                }
                type = property.PropertyType;
            }
            return type;
        }
    }

    /// <summary>"{Binding X, Converter=…}" or "{Binding Path=X}" → "X"; the bindings to another element are ignored.</summary>
    private static string? Path(string? value)
    {
        var match = Regex.Match(value ?? "", @"^\{Binding\s*(?:Path=)?([\w.]*)\s*(,(?<rest>.*))?\}$");
        if (!match.Success || match.Groups["rest"].Value.Contains("ElementName") || match.Groups["rest"].Value.Contains("RelativeSource"))
            return null;
        return match.Groups[1].Value;
    }

    /// <summary>"{d:DesignInstance Type=vm:X}" or "{x:Type vm:X}" → the type X of the ViewModels.</summary>
    private static Type? Type(string? value)
    {
        var match = Regex.Match(value ?? "", @"vm:(\w+)");
        return match.Success
            ? typeof(MainScreenViewModel).Assembly.GetType($"{typeof(MainScreenViewModel).Namespace}.{match.Groups[1].Value}", throwOnError: true)
            : null;
    }

    private static Type? ItemType(Type collection) =>
        collection.GetInterfaces().Append(collection)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
}
