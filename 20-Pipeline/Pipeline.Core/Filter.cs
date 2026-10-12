namespace Pipeline.Core;

/// <summary>
/// A FILTER: it receives an item, produces another one (or rejects it), and knows nothing else. Neither where
/// the item comes from, nor where it goes, nor which filters surround it: that is what allows reordering them, replacing them,
/// reusing them in another pipeline and testing them one by one.
/// </summary>
public interface IFilter<in TInput, TOutput>
{
    string Name { get; }

    /// <summary>A rejection is a normal result (the line is discarded, the flow goes on); an exception stops the whole pipeline.</summary>
    ValueTask<Outcome<TOutput>> Process(TInput input, CancellationToken cancellation);
}

/// <summary>What a filter does with an item: it transforms it, or it rejects it with a reason.</summary>
public readonly record struct Outcome<T>
{
    private Outcome(T? value, string? reason, string? detail)
    {
        Value = value;
        Reason = reason;
        Detail = detail;
    }

    public T? Value { get; }

    /// <summary>The reason for the rejection, short and stable (it is used to count the rejections by reason).</summary>
    public string? Reason { get; }

    public string? Detail { get; }

    public bool IsRejection => Reason is not null;

    public static Outcome<T> Ok(T value) => new(value, null, null);

    public static Outcome<T> Reject(string reason, string? detail = null) => new(default, reason, detail);
}

/// <summary>A filter written in one line, from a function (for the tests, or small composition filters).</summary>
public sealed class FunctionFilter<TInput, TOutput>(string name, Func<TInput, Outcome<TOutput>> function) : IFilter<TInput, TOutput>
{
    public string Name => name;

    public ValueTask<Outcome<TOutput>> Process(TInput input, CancellationToken cancellation) => ValueTask.FromResult(function(input));
}

/// <summary>An item flowing in the pipes: its value, and the original line number (for the rejections).</summary>
public sealed record Element<T>(long Line, T Value);

/// <summary>An item discarded by a filter.</summary>
public sealed record Rejection(long Line, string Filter, string Reason, string? Detail);
