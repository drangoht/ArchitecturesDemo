using Modular.Shared;

namespace Modular.Transfers.Contracts;

/// <summary>
/// The event carries everything the subscribers need (including the owners' names),
/// so they don't have to call the Accounts module back: "event-carried state transfer".
/// </summary>
public record TransferMade(
    Guid SourceId, string SourceOwner,
    Guid TargetId, string TargetOwner,
    decimal Amount) : IIntegrationEvent;
