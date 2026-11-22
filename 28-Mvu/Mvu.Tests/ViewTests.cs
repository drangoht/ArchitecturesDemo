using Mvu.Bank;
using Mvu.Runtime;
using H = Mvu.Runtime.Elements<Mvu.Bank.Msg>;

namespace Mvu.Tests;

/// <summary>The view is a pure function too: a model in, a description out. Its buttons carry their messages.</summary>
public class ViewTests
{
    private static Button<Msg> TransferButton(Model model) => (Button<Msg>)H.Find(BankView.View(model), "transfer")!;

    [Fact]
    public void The_transfer_button_is_greyed_out_until_the_form_is_valid()
    {
        Assert.False(TransferButton(UpdateTests.Loaded).Enabled);
        Assert.False(TransferButton(UpdateTests.Apply(UpdateTests.Loaded, new SourceChosen(0), new TargetChosen(2), new AmountTyped("abc"))).Enabled);

        var valid = TransferButton(UpdateTests.Apply(UpdateTests.Loaded, new SourceChosen(0), new TargetChosen(2), new AmountTyped("250")));

        Assert.Equal(new TransferClicked(), valid.OnClick);
    }

    [Fact]
    public void The_errors_of_the_model_are_lines_of_the_view()
    {
        var model = UpdateTests.Apply(UpdateTests.Loaded, new SourceChosen(0), new TargetChosen(0), new AmountTyped("abc"));

        var lines = Lines(BankView.View(model));

        Assert.Contains("⚠ Enter an amount, for example 250.50.", lines);
        Assert.Contains("⚠ Choose two different accounts.", lines);
    }

    [Fact]
    public void Typing_in_the_amount_field_produces_a_message()
    {
        var box = (TextBox<Msg>)H.Find(BankView.View(UpdateTests.Loaded), "amount")!;

        Assert.Equal(new AmountTyped("250"), box.OnInput("250"));
    }

    [Fact]
    public void A_pending_question_shows_yes_and_no_and_greys_out_the_transfer()
    {
        var asking = UpdateTests.Apply(UpdateTests.Loaded, new SourceChosen(1), new TargetChosen(2), new AmountTyped("1000"), new TransferClicked());
        var view = BankView.View(asking);

        Assert.Equal(new ConfirmationAnswered(true), ((Button<Msg>)H.Find(view, "yes")!).OnClick);
        Assert.Equal(new ConfirmationAnswered(false), ((Button<Msg>)H.Find(view, "no")!).OnClick);
        Assert.False(TransferButton(asking).Enabled);
    }

    [Fact]
    public void While_the_operations_load_the_view_says_so()
    {
        Assert.Contains("  loading…", Lines(BankView.View(UpdateTests.Loaded)));
    }

    public static List<string> Lines(Node<Msg> node) =>
        node is Column<Msg> column ? [.. column.Children.SelectMany(Lines)] : [node.Describe()];
}
