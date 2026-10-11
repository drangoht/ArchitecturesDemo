using Ihm.Banque;
using Ihm.Console;
using Ihm.Presentation;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

// Le même assemblage que la fenêtre WPF (App.xaml.cs), avec une autre vue et une autre façon de poser les questions.
var banque = new BanqueEnMemoire(TimeProvider.System, TimeSpan.FromMilliseconds(300));
var ecran = new EcranPrincipalViewModel(banque, new ConfirmationConsole(Console.In, Console.Out));
using var vue = new VueConsole(ecran, Console.In, Console.Out);
await vue.ExecuterAsync();
