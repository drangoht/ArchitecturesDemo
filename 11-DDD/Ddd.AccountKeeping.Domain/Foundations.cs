using Ddd.AccountKeeping.Domain.ValueObjects;

namespace Ddd.AccountKeeping.Domain;

public class BusinessRuleException(string message) : Exception(message);

public class AccountNotFoundException(string description) : Exception($"Account not found: {description}.");

/// <summary>Domain event: a business fact, internal to this bounded context, expressed with its value objects.</summary>
public interface IDomainEvent
{
    DateTime Date { get; }
}

public record AccountOpened(AccountId AccountId, Iban Iban, OwnerName Owner, Money InitialDeposit, DateTime Date) : IDomainEvent;

public record WithdrawalMade(AccountId AccountId, Money Amount, DateTime Date) : IDomainEvent;

public record TransferMade(
    Guid TransferId, AccountId Source, Iban SourceIban, OwnerName SourceOwner,
    Iban TargetIban, Money DebitedAmount, Money CreditedAmount, DateTime Date) : IDomainEvent;

public record AccountBlocked(AccountId AccountId, string Reason, DateTime Date) : IDomainEvent;

/// <summary>An aggregate root accumulates the domain events it raises, until they are published.</summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    protected void Raise(IDomainEvent @event) => _events.Add(@event);

    public IReadOnlyList<IDomainEvent> ExtractEvents()
    {
        var events = _events.ToList();
        _events.Clear();
        return events;
    }
}
