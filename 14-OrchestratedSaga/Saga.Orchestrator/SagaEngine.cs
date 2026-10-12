using Saga.Participants;

namespace Saga.Orchestrator;

/// <summary>
/// The orchestration engine. On each call to Process(), it moves each ready saga forward by ONE step,
/// and saves its new state. It keeps nothing in memory: it can be stopped and restarted at any time.
/// </summary>
public sealed class SagaEngine(SagaStore store, TransferDefinition definition, TimeProvider clock, SagaOptions options)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<Step> Steps => definition.Steps;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public IEnumerable<string> StepNames => Steps.Select(e => e.Name);

    public SagaState Start(Guid source, Guid target, decimal amount)
    {
        var saga = new SagaState { Id = Guid.NewGuid(), Source = source, Target = target, Amount = amount, NextAttempt = Now };
        Note(saga, $"▶ Transfer of {amount:0.00} requested");
        store.Save(saga);
        return saga;
    }

    /// <summary>Moves all the ready sagas forward by one step. Returns the number of sagas processed.</summary>
    public async Task<int> Process()
    {
        await _lock.WaitAsync();
        try
        {
            var ready = store.All().Where(IsReady).ToList();
            foreach (var saga in ready)
            {
                await Advance(saga);
                store.Save(saga);  // the state is saved after EACH step
            }
            return ready.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>External event: the compliance analyst's decision.</summary>
    public async Task<bool> Signal(Guid id, bool approved, string? comment)
    {
        await _lock.WaitAsync();
        try
        {
            var saga = store.Load(id);
            if (saga is not { Status: SagaStatus.Waiting })
                return false;

            if (approved)
            {
                Note(saga, $"✉ Decision received: approved{Comment(comment)}");
                CompleteStep(saga, Steps[saga.CurrentStep]);
            }
            else
            {
                StartCompensation(saga, $"Refused by the compliance analyst{Comment(comment)}");
            }
            store.Save(saga);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsReady(SagaState saga) => saga.Status switch
    {
        SagaStatus.Waiting => saga.Deadline <= Now,  // only a missed deadline wakes it up
        _ => !saga.IsFinished && saga.NextAttempt <= Now
    };

    private Task Advance(SagaState saga)
    {
        switch (saga.Status)
        {
            case SagaStatus.Waiting:
                StartCompensation(saga, $"No decision before the deadline for {Steps[saga.CurrentStep].Name}");
                return Task.CompletedTask;
            case SagaStatus.Compensating:
                return CompensateOneStep(saga);
            default:
                return ExecuteCurrentStep(saga);
        }
    }

    private async Task ExecuteCurrentStep(SagaState saga)
    {
        if (saga.CurrentStep >= Steps.Count)
        {
            saga.Status = SagaStatus.Completed;
            Note(saga, "■ Transfer completed");
            return;
        }

        var step = Steps[saga.CurrentStep];
        Outcome outcome;
        try
        {
            outcome = await step.Execute(saga);
        }
        catch (BusinessRefusalException e)
        {
            outcome = new Outcome.Refused(e.Message);
        }
        catch (Exception e)
        {
            TransientFailure(saga, step, e.Message);
            return;
        }

        switch (outcome)
        {
            case Outcome.Succeeded:
                CompleteStep(saga, step);
                break;
            case Outcome.Waiting waiting:
                saga.Status = SagaStatus.Waiting;
                saga.Deadline = Now + waiting.Delay;
                Note(saga, $"⏳ {step.Name}: {waiting.Reason} (deadline {saga.Deadline:HH:mm:ss} UTC)");
                break;
            case Outcome.Refused refusal when PointOfNoReturnPassed(saga):
                RequireManualIntervention(saga, $"{step.Name} refused after the point of no return: {refusal.Reason}");
                break;
            case Outcome.Refused refusal:
                StartCompensation(saga, $"{step.Name} refused: {refusal.Reason}");
                break;
        }
    }

    private void TransientFailure(SagaState saga, Step step, string message)
    {
        saga.Attempts++;
        if (saga.Attempts < options.MaxAttempts)
        {
            var delay = Delay(saga.Attempts);
            saga.NextAttempt = Now + delay;
            Note(saga, $"✖ {step.Name}: {message} — attempt {saga.Attempts}/{options.MaxAttempts}, " +
                       $"retrying in {delay.TotalMilliseconds:0} ms");
        }
        else if (PointOfNoReturnPassed(saga))
        {
            // Nothing can be undone any more: the money has already been debited for good.
            RequireManualIntervention(saga, $"{step.Name} impossible after {options.MaxAttempts} attempts: {message}");
        }
        else
        {
            StartCompensation(saga, $"{step.Name} impossible after {options.MaxAttempts} attempts: {message}");
        }
    }

    /// <summary>Undoes the most recent of the successful steps. A compensation must eventually succeed: it is retried forever.</summary>
    private async Task CompensateOneStep(SagaState saga)
    {
        if (saga.CompletedSteps.Count == 0)
        {
            saga.Status = SagaStatus.Cancelled;
            Note(saga, "■ Transfer cancelled: all the steps were undone");
            return;
        }

        var step = Steps.Single(e => e.Name == saga.CompletedSteps[^1]);
        if (step.Compensate is not null)
        {
            try
            {
                await step.Compensate(saga);
                Note(saga, $"↩ {step.Name} undone");
            }
            catch (Exception e)
            {
                saga.Attempts++;
                saga.NextAttempt = Now + Delay(Math.Min(saga.Attempts, 6));
                Note(saga, $"✖ Compensation of {step.Name}: {e.Message} — will be retried");
                return;
            }
        }
        saga.CompletedSteps.RemoveAt(saga.CompletedSteps.Count - 1);
        saga.Attempts = 0;
        saga.NextAttempt = Now;
    }

    private void CompleteStep(SagaState saga, Step step)
    {
        saga.CompletedSteps.Add(step.Name);
        saga.CurrentStep++;
        (saga.Status, saga.Attempts, saga.Deadline) = (SagaStatus.Running, 0, null);
        saga.NextAttempt = Now.AddMilliseconds(options.PauseBetweenStepsMs);
        Note(saga, $"✔ {step.Name}{(step.PointOfNoReturn ? " (point of no return crossed)" : "")}");
    }

    private void StartCompensation(SagaState saga, string reason)
    {
        (saga.Status, saga.Reason, saga.Attempts, saga.Deadline, saga.NextAttempt) =
            (SagaStatus.Compensating, reason, 0, null, Now);
        Note(saga, $"↩ Compensation: {reason}");
    }

    private void RequireManualIntervention(SagaState saga, string reason)
    {
        (saga.Status, saga.Reason) = (SagaStatus.ManualInterventionRequired, reason);
        Note(saga, $"⚠ {reason} — manual intervention required");
    }

    private bool PointOfNoReturnPassed(SagaState saga) =>
        Steps.Any(e => e.PointOfNoReturn && saga.CompletedSteps.Contains(e.Name));

    private TimeSpan Delay(int attempt) => TimeSpan.FromMilliseconds(options.InitialDelayMs * Math.Pow(2, attempt - 1));

    private void Note(SagaState saga, string message) => saga.Log.Add(new LogEntry(Now, message));

    private static string Comment(string? comment) => string.IsNullOrWhiteSpace(comment) ? "" : $" ({comment})";
}
