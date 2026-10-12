using Actors.Runtime;

namespace Actors.Bank;

public static class Setup
{
    /// <summary>
    /// Declares the bank's actor types and their supervision.
    /// The dependencies (storage, traps, clock) are captured by the factories: each incarnation starts from a fresh object.
    /// </summary>
    public static ActorSystem InstallBank(this ActorSystem system, StateStore store, Traps traps,
        TimeProvider clock, SupervisionStrategy? strategy = null)
    {
        system.Register(AccountActor.Type, () => new AccountActor(store, traps, clock), strategy);
        system.Register(TransferActor.Type, () => new TransferActor(store, traps), strategy);
        return system;
    }

    public static ActorRef Account(this ActorSystem system, Guid id) => system.Actor(AccountActor.Type, id);

    public static ActorRef Transfer(this ActorSystem system, string id) => system.Actor(TransferActor.Type, id);
}
