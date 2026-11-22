using Mvu.Bank;
using Mvu.Console;
using Mvu.Runtime;
using Ui.Bank;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

// The same bank as demo 24 (300 ms per call, to see the screen wait), the program, the effects, a renderer.
var bank = new InMemoryBank(TimeProvider.System, TimeSpan.FromMilliseconds(300));
var renderer = new ConsoleRenderer<Model, Msg, Effect>(Console.In, Console.Out);
Console.WriteLine("MVU Bank, console. Commands: click ID | type ID text | pick ID N | show | quit");
await using var runtime = new Runtime<Model, Msg, Effect>(BankScreen.Program, new BankEffects(bank).Perform, renderer.Render);
await renderer.RunAsync(runtime);
