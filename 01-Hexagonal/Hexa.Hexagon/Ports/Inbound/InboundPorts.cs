namespace Hexa.Hexagon.Ports.Inbound;

// Inbound ports ("driving", primary): what the application OFFERS to the outside world.
// They are called by the inbound adapters: web API, command line, tests…

public interface IGetAccount
{
    Task<AccountView> Execute(Guid accountId);
}

public interface IWithdrawMoney
{
    Task Execute(Guid accountId, decimal amount);
}

public interface IMakeTransfer
{
    Task Execute(Guid sourceId, Guid targetId, decimal amount);
}

/// <summary>What a consumer of the port sees of an account (and not the entity itself).</summary>
public record AccountView(Guid Id, string Owner, decimal Balance);
