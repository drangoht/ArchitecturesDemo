using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ui.Bank;
using Ui.Presentation;

namespace Ui.Wpf;

/// <summary>
/// The assembly point: the bank, the ViewModel, the window. It is the only place that knows all three.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["capture", var file, ..])
        {
            await Capture(file, e.Args.Skip(2).ToArray());
            Shutdown();
            return;
        }

        // 400 ms per call, like a remote service: enough to see the button grey out and the activity bar.
        var bank = new InMemoryBank(TimeProvider.System, TimeSpan.FromMilliseconds(400));
        var screen = new MainScreenViewModel(bank, new WpfConfirmation());
        var window = new MainWindow { DataContext = screen };
        MainWindow = window;
        window.Show();
        await screen.InitializeAsync();
    }

    /// <summary>
    /// Draws the window into a PNG image without displaying it (for the documentation and the checks).
    /// Optional arguments: an amount to type, then "source target" (index in the list of accounts).
    /// </summary>
    private static async Task Capture(string file, string[] input)
    {
        var bank = new InMemoryBank(TimeProvider.System);
        var screen = new MainScreenViewModel(bank, new WpfConfirmation());
        await screen.InitializeAsync();
        var transfer = screen.Transfer;
        if (input.Length >= 3)
        {
            transfer.Source = transfer.Accounts[int.Parse(input[1])];
            transfer.Target = transfer.Accounts[int.Parse(input[2])];
        }
        if (input.Length >= 1)
            transfer.AmountText = input[0];

        var window = new MainWindow { DataContext = screen };
        var content = (FrameworkElement)window.Content;
        window.Content = null;     // the content, detached from the window (never shown), is measured and drawn alone
        content.DataContext = screen;
        var size = new Size(window.Width, window.Height - 40);
        content.Measure(size);
        content.Arrange(new Rect(size));
        // Let the bindings and the item templates resolve before drawing.
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();

        var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(Brushes.White, null, new Rect(size));
        image.Render(background);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        await using var stream = File.Create(file);
        encoder.Save(stream);
    }
}

/// <summary>The ViewModel's question, asked by a dialog box: it is the view that knows how to ask.</summary>
public sealed class WpfConfirmation : IConfirmation
{
    public Task<bool> AskAsync(string question) =>
        Task.FromResult(MessageBox.Show(Application.Current.MainWindow!, question, "Confirm the transfer",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
}
