using Mvu.Runtime;

namespace Mvu.Console;

/// <summary>
/// A generic renderer, for ANY MVU program: it knows nothing about banks. It prints the view the first time, then only
/// the differences (+ added, ~ changed, - removed), and turns the user's commands into the messages the view
/// attached to its elements: "click transfer" sends the message of the button whose id is "transfer".
/// </summary>
public sealed class ConsoleRenderer<TModel, TMsg, TEffect>(TextReader input, TextWriter output)
{
    private readonly Lock _gate = new();

    public void Render(Node<TMsg> view, IReadOnlyList<Patch> patches)
    {
        lock (_gate)
        {
            var first = patches.All(p => p.Kind == PatchKind.Insert);
            foreach (var patch in patches)
                output.WriteLine(first ? $"  {patch.Line}" : $"  {Sign(patch.Kind)} {patch.Line}");
        }
    }

    public async Task RunAsync(Runtime<TModel, TMsg, TEffect> runtime)
    {
        await runtime.Settled();
        while (await input.ReadLineAsync() is { } line)
        {
            line = line.TrimStart('﻿');   // input piped from PowerShell starts with a byte order mark
            var words = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                continue;
            lock (_gate)
                output.WriteLine($"> {line.Trim()}");

            if (words[0] is "quit" or "q")
                return;
            if (words[0] == "show")
            {
                Render(runtime.View, Diff.Compute(null, runtime.View));
                continue;
            }

            var element = words.Length > 1 ? Elements<TMsg>.Find(runtime.View, words[1]) : null;
            TMsg? message = (words[0], element, words.Length > 2 ? words[2] : "") switch
            {
                ("click", Button<TMsg> button, _) => button.OnClick,
                ("type", TextBox<TMsg> box, var text) => box.OnInput(text),
                ("pick", Choice<TMsg> choice, var n) when int.TryParse(n, out var i) && i >= 1 && i <= choice.Options.Count =>
                    choice.OnSelect(i - 1),
                _ => default,
            };

            if (message is null)
            {
                lock (_gate)
                    output.WriteLine(element is Button<TMsg> { Enabled: false }
                        ? "  (greyed out: nothing happens)"
                        : "  Commands: click ID | type ID text | pick ID N | show | quit");
                continue;
            }
            runtime.Dispatch(message);
            await runtime.Settled();   // the screen settles (effects included) before the next command
        }
    }

    private static char Sign(PatchKind kind) => kind switch
    {
        PatchKind.Insert => '+',
        PatchKind.Remove => '-',
        _ => '~',
    };
}
