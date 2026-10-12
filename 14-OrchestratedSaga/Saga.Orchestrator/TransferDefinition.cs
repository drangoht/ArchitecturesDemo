using Saga.Participants;

namespace Saga.Orchestrator;

public abstract record Outcome
{
    public sealed record Succeeded : Outcome;
    public sealed record Waiting(TimeSpan Delay, string Reason) : Outcome;
    public sealed record Refused(string Reason) : Outcome;

    public static readonly Outcome Success = new Succeeded();
}

/// <param name="Compensate">The action that undoes the step (null: nothing to undo).</param>
/// <param name="PointOfNoReturn">Once this step is done, there is no more compensation: the only way is to the end.</param>
public sealed record Step(
    string Name,
    Func<SagaState, Task<Outcome>> Execute,
    Func<SagaState, Task>? Compensate = null,
    bool PointOfNoReturn = false);

public sealed class SagaOptions
{
    public int MaxAttempts { get; set; } = 4;
    public int InitialDelayMs { get; set; } = 200;
    public int PauseBetweenStepsMs { get; set; } = 300;
    public int ComplianceDecisionTimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// THE DEFINITION OF THE PROCESS, readable at a glance: that is the big advantage of orchestration.
/// With choreography (demos 7 and 8), this logic is scattered across the reactions of each service.
/// </summary>
public sealed class TransferDefinition(
    AccountsService accounts, ComplianceService compliance, ExchangeService exchange, NotificationsService notifications, SagaOptions options)
{
    public IReadOnlyList<Step> Steps { get; } =
    [
        new("ReserveFunds",
            async s => { await accounts.Reserve(s.Id, s.Source, s.Amount); return Outcome.Success; },
            Compensate: s => accounts.Release(s.Id, s.Source)),

        new("CheckCompliance", async s =>
            await compliance.Examine(s.Id, ExchangeService.InEuros(s.Amount, accounts.Currency(s.Source))) switch
            {
                ComplianceOpinion.Approved => Outcome.Success,
                ComplianceOpinion.AwaitingAnalyst => new Outcome.Waiting(
                    TimeSpan.FromSeconds(options.ComplianceDecisionTimeoutSeconds), "awaiting an analyst's decision"),
                _ => new Outcome.Refused("Refused by compliance.")
            }),

        new("ConvertCurrencies",
            async s =>
            {
                var (from, to) = (accounts.Currency(s.Source), accounts.Currency(s.Target));
                s.TargetCurrency = to;
                s.CreditedAmount = from == to ? s.Amount : await exchange.Convert(s.Id, s.Amount, from, to);
                return Outcome.Success;
            },
            Compensate: s => exchange.CancelQuote(s.Id)),

        new("CreditBeneficiary",
            async s => { await accounts.Credit(s.Id, s.Target, s.CreditedAmount!.Value); return Outcome.Success; },
            Compensate: s => accounts.CancelCredit(s.Id, s.Target)),

        new("CaptureFunds",
            async s => { await accounts.Capture(s.Id, s.Source); return Outcome.Success; },
            PointOfNoReturn: true),

        new("Notify", async s =>
        {
            await notifications.Send(s.Id,
                $"Transfer of {s.Amount:0.00} {accounts.Currency(s.Source)} done ({s.CreditedAmount:0.00} {s.TargetCurrency} credited).");
            return Outcome.Success;
        })
    ];
}
