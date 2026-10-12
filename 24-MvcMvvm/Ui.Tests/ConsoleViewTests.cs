using Ui.Bank;
using Ui.Console;
using Ui.Presentation;

namespace Ui.Tests;

/// <summary>The second view: the same logic, plugged in differently. And what a binding is, taken apart.</summary>
public class ConsoleViewTests
{
    [Fact]
    public void A_binding_copies_the_property_at_each_change_and_no_more_after_Dispose()
    {
        var transfer = new MainScreenViewModel(new InMemoryBank(new FixedClock()), new ProgrammedConfirmation(true)).Transfer;
        var displayed = new List<object?>();

        var binding = Binding.Bind(transfer, nameof(TransferViewModel.AmountText), displayed.Add);
        transfer.AmountText = "12";
        transfer.Target = null;                  // another property: ignored
        transfer.AmountText = "13";
        binding.Dispose();
        transfer.AmountText = "14";

        Assert.Equal(["", "12", "13"], displayed);   // the initial value, then each change
    }

    [Fact]
    public void A_binding_to_a_property_that_does_not_exist_fails_clearly()
    {
        var transfer = new MainScreenViewModel(new InMemoryBank(new FixedClock()), new ProgrammedConfirmation(true)).Transfer;

        var error = Assert.Throws<ArgumentException>(() => Binding.Bind(transfer, "Amount", _ => { }));
        Assert.StartsWith("TransferViewModel has no property \"Amount\".", error.Message);
    }

    [Fact]
    public async Task The_console_drives_the_same_ViewModels_as_the_WPF_window()
    {
        var input = new StringReader(string.Join('\n', "from 2", "to 3", "amount 1500", "transfer", "n", "amount 25", "transfer", "show 3", "quit"));
        var output = new StringWriter();
        var bank = new InMemoryBank(new FixedClock());
        var screen = new MainScreenViewModel(bank, new ConsoleConfirmation(input, output));
        using var view = new ConsoleView(screen, input, output);

        await view.RunAsync();

        var lines = output.ToString().Split(Environment.NewLine).Select(l => l.TrimEnd()).ToArray();
        string[] expected =
        [
            "> amount 1500",
            "  [Transfer] enabled",                                  // pushed by CanExecuteChanged, without being asked
            "> transfer",
            "  [Transfer] greyed out",                               // during the execution
            "  ? Transfer 1,500.00 € from Alice — Savings account to Bob — Current account? (y/n)",
            "> n",
            "  » Transfer cancelled.",                               // pushed by the binding on Message
            "  [Transfer] enabled",                                  // the command is over
            "> amount 25",
            "> transfer",
            "  [Transfer] greyed out",
            "  » Transfer in progress…",
            "  » Transfer of 25.00 € done. New balance: 7,975.00 €.",
            "> show 3",
            "  Operations — Bob — Current account",
        ];
        Assert.Equal(expected, lines.SkipWhile(l => l != "> amount 1500").Take(expected.Length));
        Assert.Contains(lines, l => l.StartsWith("    11/10/2026 14:30  Transfer from Alice (Savings account)") && l.EndsWith("+25.00 €"));
        Assert.Equal(525m, (await bank.AccountAsync(InMemoryBank.Bob)).Balance);
    }
}
