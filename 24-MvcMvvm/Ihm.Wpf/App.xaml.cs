using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ihm.Banque;
using Ihm.Presentation;

namespace Ihm.Wpf;

/// <summary>
/// Le point d'assemblage : la banque, le ViewModel, la fenêtre. C'est le seul endroit qui connaît les trois.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["capture", var fichier, ..])
        {
            await Capturer(fichier, e.Args.Skip(2).ToArray());
            Shutdown();
            return;
        }

        // 400 ms par appel, comme un service distant : de quoi voir le bouton se griser et la barre d'activité.
        var banque = new BanqueEnMemoire(TimeProvider.System, TimeSpan.FromMilliseconds(400));
        var ecran = new EcranPrincipalViewModel(banque, new ConfirmationWpf());
        var fenetre = new MainWindow { DataContext = ecran };
        MainWindow = fenetre;
        fenetre.Show();
        await ecran.InitialiserAsync();
    }

    /// <summary>
    /// Dessine la fenêtre dans une image PNG sans l'afficher (pour la documentation et les vérifications).
    /// Arguments facultatifs : un montant à saisir, puis « source cible » (index dans la liste des comptes).
    /// </summary>
    private static async Task Capturer(string fichier, string[] saisie)
    {
        var banque = new BanqueEnMemoire(TimeProvider.System);
        var ecran = new EcranPrincipalViewModel(banque, new ConfirmationWpf());
        await ecran.InitialiserAsync();
        var virement = ecran.Virement;
        if (saisie.Length >= 3)
        {
            virement.Source = virement.Comptes[int.Parse(saisie[1])];
            virement.Cible = virement.Comptes[int.Parse(saisie[2])];
        }
        if (saisie.Length >= 1)
            virement.MontantSaisi = saisie[0];

        var fenetre = new MainWindow { DataContext = ecran };
        var contenu = (FrameworkElement)fenetre.Content;
        fenetre.Content = null;     // le contenu, détaché de la fenêtre (jamais montrée), est mesuré et dessiné seul
        contenu.DataContext = ecran;
        var taille = new Size(fenetre.Width, fenetre.Height - 40);
        contenu.Measure(taille);
        contenu.Arrange(new Rect(taille));
        // Laisser les liaisons et les modèles d'éléments se résoudre avant de dessiner.
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        contenu.UpdateLayout();

        var image = new RenderTargetBitmap((int)taille.Width, (int)taille.Height, 96, 96, PixelFormats.Pbgra32);
        var fond = new DrawingVisual();
        using (var dessin = fond.RenderOpen())
            dessin.DrawRectangle(Brushes.White, null, new Rect(taille));
        image.Render(fond);
        image.Render(contenu);
        var encodeur = new PngBitmapEncoder();
        encodeur.Frames.Add(BitmapFrame.Create(image));
        await using var flux = File.Create(fichier);
        encodeur.Save(flux);
    }
}

/// <summary>La question du ViewModel, posée par une boîte de dialogue : c'est la vue qui sait comment demander.</summary>
public sealed class ConfirmationWpf : IConfirmation
{
    public Task<bool> DemanderAsync(string question) =>
        Task.FromResult(MessageBox.Show(Application.Current.MainWindow!, question, "Confirmer le virement",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
}
