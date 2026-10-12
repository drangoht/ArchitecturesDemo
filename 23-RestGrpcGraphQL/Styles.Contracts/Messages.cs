using Styles.Tooling.Protobuf;

namespace Styles.Contracts;

// The messages of bank.proto, as protoc would generate them (simplified). A test checks the correspondence.

public static class BankGrpcService
{
    /// <summary>The full name of the service: "package.Service", the first part of the HTTP path of each method.</summary>
    public const string Name = "bank.Bank";
}

public enum AccountType { UnknownAccountType = 0, Current = 1, Savings = 2 }

public sealed class DashboardRequest
{
    [Field(1)] public string CustomerId { get; set; } = "";
    [Field(2)] public int OperationsPerAccount { get; set; }
}

public sealed class Dashboard
{
    [Field(1)] public string CustomerId { get; set; } = "";
    [Field(2)] public string Name { get; set; } = "";
    [Field(3)] public List<Account> Accounts { get; set; } = [];
}

public sealed class AccountRequest
{
    [Field(1)] public string Number { get; set; } = "";
}

public sealed class Account
{
    [Field(1)] public string Number { get; set; } = "";
    [Field(2)] public string Label { get; set; } = "";
    [Field(3)] public AccountType Type { get; set; }
    [Field(4)] public long BalanceCents { get; set; }
    [Field(5)] public List<Operation> LatestOperations { get; set; } = [];
}

public sealed class Operation
{
    [Field(1)] public string Id { get; set; } = "";
    [Field(2)] public long DateUnixMs { get; set; }
    [Field(3)] public string Label { get; set; } = "";
    [Field(4, ZigZag = true)] public long AmountCents { get; set; }
}

public sealed class TransferRequest
{
    [Field(1)] public string Source { get; set; } = "";
    [Field(2)] public string Target { get; set; } = "";
    [Field(3)] public long AmountCents { get; set; }
    [Field(4)] public string Label { get; set; } = "";
}

public sealed class Transfer
{
    [Field(1)] public string Id { get; set; } = "";
    [Field(2)] public string Source { get; set; } = "";
    [Field(3)] public string Target { get; set; } = "";
    [Field(4)] public long AmountCents { get; set; }
    [Field(5)] public long SourceBalanceCents { get; set; }
}

public sealed class FollowRequest
{
    [Field(1)] public string Number { get; set; } = "";
}
