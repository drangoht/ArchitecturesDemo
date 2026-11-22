using Mvu.Runtime;
using H = Mvu.Runtime.Elements<Mvu.Tests.RuntimeTests.CounterMsg>;

namespace Mvu.Tests;

/// <summary>The engine alone, with a toy program (a counter): the mailbox, the effects, the diff.</summary>
public class RuntimeTests
{
    public abstract record CounterMsg;
    public sealed record Add(int N) : CounterMsg;
    public sealed record Loaded(int Value) : CounterMsg;
    public sealed record Rename(string Name) : CounterMsg;

    public sealed record Counter(int Value, string Name);

    public abstract record CounterEffect;
    public sealed record Fetch : CounterEffect;
    public sealed record Explode : CounterEffect;

    private static int _inUpdate;

    private static Program<Counter, CounterMsg, CounterEffect> Program(params CounterEffect[] atStart) => new(
        () => (new Counter(0, "counter"), atStart),
        (message, model) =>
        {
            // If two messages were ever handled at the same time, this would see it.
            if (Interlocked.Increment(ref _inUpdate) != 1)
                throw new InvalidOperationException("Update called concurrently.");
            try
            {
                return message switch
                {
                    Add add => (model with { Value = model.Value + add.N }, []),
                    Loaded loaded => (model with { Value = loaded.Value }, []),
                    Rename rename => (model with { Name = rename.Name }, []),
                    _ => (model, []),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inUpdate);
            }
        },
        model => H.Column(H.Text(model.Name), H.Text($"value: {model.Value}")));

    private static Task<CounterMsg> Perform(CounterEffect effect, CancellationToken ct) => effect switch
    {
        Fetch => Task.FromResult<CounterMsg>(new Loaded(42)),
        _ => throw new InvalidOperationException("Boom."),
    };

    [Fact]
    public async Task Messages_sent_from_many_threads_are_handled_one_at_a_time_and_none_is_lost()
    {
        await using var runtime = new Runtime<Counter, CounterMsg, CounterEffect>(Program(), Perform, (_, _) => { });

        await Parallel.ForAsync(0, 1_000, (_, _) => { runtime.Dispatch(new Add(1)); return ValueTask.CompletedTask; });
        await runtime.Settled().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1_000, runtime.Model.Value);
        Assert.Empty(runtime.Faults);
    }

    [Fact]
    public async Task The_result_of_an_effect_comes_back_as_a_message()
    {
        await using var runtime = new Runtime<Counter, CounterMsg, CounterEffect>(Program(new Fetch()), Perform, (_, _) => { });

        await runtime.Settled().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(42, runtime.Model.Value);
    }

    [Fact]
    public async Task The_view_is_recomputed_but_only_what_changed_reaches_the_renderer()
    {
        var renders = new List<IReadOnlyList<Patch>>();
        await using var runtime = new Runtime<Counter, CounterMsg, CounterEffect>(Program(), Perform, (_, patches) => renders.Add(patches));

        runtime.Dispatch(new Add(5));
        runtime.Dispatch(new Add(0));   // same view: nothing to render
        await runtime.Settled().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, renders.Count);
        Assert.Equal(2, renders[0].Count);   // first render: everything is inserted
        Assert.Equal(new Patch("0/1", PatchKind.Replace, "value: 5"), renders[1].Single());
    }

    [Fact]
    public async Task A_failing_effect_is_recorded_and_the_screen_keeps_going()
    {
        await using var runtime = new Runtime<Counter, CounterMsg, CounterEffect>(Program(new Explode()), Perform, (_, _) => { });

        await runtime.Settled().WaitAsync(TimeSpan.FromSeconds(5));
        runtime.Dispatch(new Add(3));
        await runtime.Settled().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Boom.", runtime.Faults.Single().Message);
        Assert.Equal(3, runtime.Model.Value);
    }

    [Fact]
    public void The_diff_inserts_removes_and_replaces_by_position()
    {
        var before = H.Column(H.Text("a"), H.Text("b"), H.Text("c"));
        var after = H.Column(H.Text("a"), H.Text("B"));

        Assert.Equal(
            [new Patch("0/1", PatchKind.Replace, "B"), new Patch("0/2", PatchKind.Remove, "c")],
            Diff.Compute(before, after));
        Assert.Equal(3, Diff.Compute(null, before).Count(p => p.Kind == PatchKind.Insert));
        Assert.Empty(Diff.Compute(before, H.Column(H.Text("a"), H.Text("b"), H.Text("c"))));
    }

    [Fact]
    public void When_a_line_becomes_a_group_the_diff_shows_its_lines()
    {
        var loading = H.Column(H.Text("title"), H.Text("loading…"));
        var loaded = H.Column(H.Text("title"), H.Column(H.Text("line 1"), H.Text("line 2")));

        Assert.Equal(
            [new Patch("0/1", PatchKind.Remove, "loading…"), new Patch("0/1/0", PatchKind.Insert, "line 1"), new Patch("0/1/1", PatchKind.Insert, "line 2")],
            Diff.Compute(loading, loaded));
    }
}
