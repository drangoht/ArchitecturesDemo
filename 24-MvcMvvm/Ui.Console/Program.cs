using Ui.Bank;
using Ui.Console;
using Ui.Presentation;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

// The same assembly as the WPF window (App.xaml.cs), with another view and another way of asking the questions.
var bank = new InMemoryBank(TimeProvider.System, TimeSpan.FromMilliseconds(300));
var screen = new MainScreenViewModel(bank, new ConsoleConfirmation(Console.In, Console.Out));
using var view = new ConsoleView(screen, Console.In, Console.Out);
await view.RunAsync();
