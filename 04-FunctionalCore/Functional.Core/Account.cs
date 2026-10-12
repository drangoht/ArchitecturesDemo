namespace Functional.Core;

/// <summary>Immutable value: an account is never modified, a new version of it is produced.</summary>
public sealed record Account(Guid Id, string Owner, decimal Balance);
