namespace Hexa.Hexagon.Domain;

/// <summary>A business rule was violated (insufficient balance, invalid amount…).</summary>
public class BusinessRuleException(string message) : Exception(message);

/// <summary>The requested account does not exist.</summary>
public class AccountNotFoundException(Guid accountId) : Exception($"Account {accountId} not found.")
{
    public Guid AccountId { get; } = accountId;
}
