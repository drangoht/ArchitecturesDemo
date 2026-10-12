namespace Layered.Business;

/// <summary>What the business layer returns to the presentation (never the data layer's entity).</summary>
public record AccountDto(Guid Id, string Owner, decimal Balance);

public class BusinessRuleException(string message) : Exception(message);

public class AccountNotFoundException(Guid accountId) : Exception($"Account {accountId} not found.");
