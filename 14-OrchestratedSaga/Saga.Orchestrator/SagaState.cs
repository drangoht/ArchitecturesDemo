using System.Collections.Concurrent;
using System.Text.Json;

namespace Saga.Orchestrator;

public enum SagaStatus
{
    Running,
    Waiting,                    // waits for an external event (an analyst's decision), with a deadline
    Compensating,               // undoes the successful steps, from the most recent to the oldest
    Completed,
    Cancelled,                  // everything was cleanly undone
    ManualInterventionRequired  // failure AFTER the point of no return: a human must step in
}

public record LogEntry(DateTime Date, string Message);

/// <summary>
/// The complete state of a saga: it is ALL the orchestrator needs to resume.
/// It is saved after each transition; the orchestrator itself keeps nothing in memory.
/// </summary>
public sealed class SagaState
{
    public Guid Id { get; set; }
    public Guid Source { get; set; }
    public Guid Target { get; set; }
    public decimal Amount { get; set; }

    public SagaStatus Status { get; set; }
    public int CurrentStep { get; set; }
    public List<string> CompletedSteps { get; set; } = [];
    public int Attempts { get; set; }
    public DateTime NextAttempt { get; set; }
    public DateTime? Deadline { get; set; }
    public string? Reason { get; set; }

    // Data produced by the steps and used by the next ones.
    public string? TargetCurrency { get; set; }
    public decimal? CreditedAmount { get; set; }

    public List<LogEntry> Log { get; set; } = [];

    public bool IsFinished => Status is SagaStatus.Completed or SagaStatus.Cancelled or SagaStatus.ManualInterventionRequired;
}

/// <summary>
/// Storage of the sagas' state. The states are kept SERIALIZED (as in a database):
/// each read produces a fresh copy, no object is shared with the orchestrator.
/// </summary>
public sealed class SagaStore
{
    private readonly ConcurrentDictionary<Guid, string> _sagas = new();

    public void Save(SagaState saga) => _sagas[saga.Id] = JsonSerializer.Serialize(saga);

    public SagaState? Load(Guid id) =>
        _sagas.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<SagaState>(json) : null;

    public IReadOnlyList<SagaState> All() =>
        _sagas.Values.Select(json => JsonSerializer.Deserialize<SagaState>(json)!).ToList();
}
