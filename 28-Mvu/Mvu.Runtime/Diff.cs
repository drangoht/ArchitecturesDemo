namespace Mvu.Runtime;

public enum PatchKind { Insert, Remove, Replace }

/// <summary>One change to apply to the screen: at this place in the tree (indexes from the root), this line.</summary>
public sealed record Patch(string Path, PatchKind Kind, string Line);

/// <summary>
/// Compares two views, position by position: the "virtual DOM" in miniature. The view is recomputed in full,
/// but only what changed reaches the screen (React, Elm and Blazor do this, with keys and much more care).
/// </summary>
public static class Diff
{
    public static IReadOnlyList<Patch> Compute<TMsg>(Node<TMsg>? before, Node<TMsg> after)
    {
        var patches = new List<Patch>();
        Compare(before, after, "0", patches);
        return patches;
    }

    private static void Compare<TMsg>(Node<TMsg>? before, Node<TMsg>? after, string path, List<Patch> patches)
    {
        switch (before, after)
        {
            case (null, null):
                return;
            case (null, not null):
                Insert(after, path, patches);
                return;
            case (not null, null):
                Remove(before, path, patches);
                return;
            case (Column<TMsg> left, Column<TMsg> right):
                var count = Math.Max(left.Children.Count, right.Children.Count);
                for (var i = 0; i < count; i++)
                    Compare(left.Children.ElementAtOrDefault(i), right.Children.ElementAtOrDefault(i), $"{path}/{i}", patches);
                return;
            case (Column<TMsg>, _) or (_, Column<TMsg>):
                // A line became a group, or the reverse ("loading…" replaced by the list): out with the old, in with the new.
                Remove(before, path, patches);
                Insert(after, path, patches);
                return;
            default:
                if (before.GetType() != after.GetType() || before.Describe() != after.Describe())
                    patches.Add(new Patch(path, PatchKind.Replace, after.Describe()));
                return;
        }
    }

    private static void Remove<TMsg>(Node<TMsg> node, string path, List<Patch> patches)
    {
        if (node is Column<TMsg> column)
            for (var i = 0; i < column.Children.Count; i++)
                Remove(column.Children[i], $"{path}/{i}", patches);
        else
            patches.Add(new Patch(path, PatchKind.Remove, node.Describe()));
    }

    private static void Insert<TMsg>(Node<TMsg> node, string path, List<Patch> patches)
    {
        if (node is Column<TMsg> column)
            for (var i = 0; i < column.Children.Count; i++)
                Insert(column.Children[i], $"{path}/{i}", patches);
        else
            patches.Add(new Patch(path, PatchKind.Insert, node.Describe()));
    }
}
