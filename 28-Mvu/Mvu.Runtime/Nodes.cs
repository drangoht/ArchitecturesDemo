namespace Mvu.Runtime;

/// <summary>
/// What a view returns: a description of the screen, not the screen. A tree of immutable values, recomputed in full
/// from the model after each message; the renderer compares it with the previous one and only applies the differences.
/// The messages to send on a click or a keystroke are part of the description.
/// </summary>
public abstract record Node<TMsg>
{
    /// <summary>What the node shows, as one line of text: what the diff compares and what the console prints.</summary>
    public abstract string Describe();
}

public sealed record Column<TMsg>(IReadOnlyList<Node<TMsg>> Children) : Node<TMsg>
{
    public override string Describe() => $"column ({Children.Count})";
}

public enum Tone { Normal, Title, Muted, Error, Success }

public sealed record Text<TMsg>(string Content, Tone Tone = Tone.Normal) : Node<TMsg>
{
    public override string Describe() => Tone switch
    {
        Tone.Title => $"== {Content} ==",
        Tone.Error => $"⚠ {Content}",
        Tone.Success => $"✓ {Content}",
        Tone.Muted => $"  {Content}",
        _ => Content,
    };
}

/// <summary>A button: OnClick is the message it sends; null means greyed out.</summary>
public sealed record Button<TMsg>(string Id, string Label, TMsg? OnClick) : Node<TMsg>
{
    public bool Enabled => OnClick is not null;

    public override string Describe() => Enabled ? $"[{Id}] {Label}" : $"({Id}) {Label}, greyed out";
}

/// <summary>A text field: what it shows comes from the model, each keystroke becomes a message.</summary>
public sealed record TextBox<TMsg>(string Id, string Label, string Value, Func<string, TMsg> OnInput) : Node<TMsg>
{
    public override string Describe() => $"[{Id}] {Label}: {(Value.Length == 0 ? "…" : Value)}";
}

/// <summary>A list to choose from: the selected index comes from the model, a choice becomes a message.</summary>
public sealed record Choice<TMsg>(string Id, string Label, IReadOnlyList<string> Options, int? Selected, Func<int, TMsg> OnSelect)
    : Node<TMsg>
{
    public override string Describe() =>
        $"[{Id}] {Label}: " + (Options.Count == 0
            ? "—"
            : string.Join("  ", Options.Select((option, i) => $"{(i == Selected ? "*" : "")}{i + 1}. {option}")));
}

/// <summary>Short constructors for the views: <c>using H = Mvu.Runtime.Elements&lt;Msg&gt;;</c> then H.Column(…).</summary>
public static class Elements<TMsg>
{
    public static Column<TMsg> Column(params IEnumerable<Node<TMsg>?> children) => new([.. children.OfType<Node<TMsg>>()]);

    public static Text<TMsg> Text(string content, Tone tone = Tone.Normal) => new(content, tone);

    public static Button<TMsg> Button(string id, string label, TMsg? onClick) => new(id, label, onClick);

    public static TextBox<TMsg> TextBox(string id, string label, string value, Func<string, TMsg> onInput) =>
        new(id, label, value, onInput);

    public static Choice<TMsg> Choice(string id, string label, IReadOnlyList<string> options, int? selected, Func<int, TMsg> onSelect) =>
        new(id, label, options, selected, onSelect);

    /// <summary>The interactive element with this id, wherever it is in the tree.</summary>
    public static Node<TMsg>? Find(Node<TMsg> root, string id) => root switch
    {
        Button<TMsg> b when b.Id == id => b,
        TextBox<TMsg> t when t.Id == id => t,
        Choice<TMsg> c when c.Id == id => c,
        Column<TMsg> column => column.Children.Select(child => Find(child, id)).FirstOrDefault(found => found is not null),
        _ => null,
    };
}
