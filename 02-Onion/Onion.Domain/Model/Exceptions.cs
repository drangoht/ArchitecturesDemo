namespace Onion.Domain.Model;

public class BusinessRuleException(string message) : Exception(message);

public class AccountNotFoundException(Guid accountId) : Exception($"Account {accountId} not found.");
