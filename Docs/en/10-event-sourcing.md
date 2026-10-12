# 10. Event Sourcing

> **In one sentence**: you don't store the state, but **the sequence of events** that led to it; the state is
> recomputed by replaying them, and nothing is ever deleted.

📁 Code: [`10-EventSourcing`](../../10-EventSourcing) · ⬅️ [CQRS](09-cqrs.md) · ➡️ [DDD](11-ddd.md) ·
🌐 [Français](../10-event-sourcing.md)

---

## The idea

In every other demo, you store **the balance** and forget how you got there. Here, you store the **facts**: the account
was opened with €1,000, €100 was withdrawn, €250 was transferred… The balance is just a **calculation**. This is exactly
how accounting works: you never erase an entry, you add another one.

```mermaid
flowchart LR
    E1["v1 · CompteOuvert<br/>Alice, €1,000"] --> E2["v2 · ArgentRetire<br/>€100, 'Courses'"]
    E2 --> E3["v3 · ArgentRetire<br/>€300, 'Loyer'"]
    E3 --> E4["v4 · VirementEmis<br/>€250 to Bob"]
    E4 -. "replay v1→v4" .-> S["Current state<br/>balance: €350<br/>version: 4"]
    E2 -. "replay v1→v2" .-> T["State at version 2<br/>balance: €900"]

    classDef evt fill:#fff8c5,stroke:#9a6700
    class E1,E2,E3,E4 evt
```

### The lifecycle of a command

```mermaid
flowchart TB
    C["Command<br/>POST /comptes/alice/retrait"] --> L["① LOAD<br/>read Alice's stream<br/>(from the last snapshot)"]
    L --> R["② REBUILD<br/>Appliquer each event<br/>→ balance, version"]
    R --> D["③ DECIDE<br/>Retirer() checks the rules<br/>→ new ArgentRetire event"]
    D --> A["④ APPEND to the store<br/>if the version is still<br/>the one that was loaded"]
    A -->|"version has changed"| X["409 Conflict<br/>nothing is written"]
    A -->|"ok"| M[("Store<br/>evenements.jsonl<br/>(append only)")]
    M --> P["Projections<br/>(statements, largest withdrawals…)"]
```

---

## Solution layout

| Project | Contents |
|---|---|
| `Es.Domaine` | Events, `Compte` aggregate (decide / apply), `InstantaneCompte` (snapshot) |
| `Es.Stockage` | `MagasinEvenements` (append only, optimistic concurrency, `.jsonl` file), `SerialiseurEvenements` (+ upcasting), `DepotComptes` (snapshots, time travel) |
| `Es.Projections` | `ProjectionReleves`, `ProjectionPlusGrosRetraits`, `MoteurProjections` (tracking, rebuild) |
| `Es.Api` | Commands, audit, time travel, administration |

**The database is a readable file**: `Es.Api\evenements.jsonl`, one line per event.

```json
{"position":3,"stream":"1111…","version":2,"type":"MoneyWithdrawn.v2","data":{"accountId":"1111…","amount":100,"reason":"Groceries","date":"…"},"date":"…"}
```

---

## The code, step by step

The code is quoted exactly as it is, French comments and messages included.

### 1. Decide / apply: two kinds of methods, never to be mixed

The class comment says it: the DECISIONS (`Retirer`, `Cloturer`…) check the rules then record an event; `Appliquer`
evolves the state from an event, without any rule (a past fact cannot be refused). Loading an account = replaying
`Appliquer` over its whole history.
([Compte.cs](../../10-EventSourcing/Es.Domaine/Compte.cs))

```csharp
/// <summary>
/// "Event-sourced" aggregate. Two kinds of methods, never to be mixed:
/// - the DECISIONS (Withdraw, Close…) check the rules then record an event;
/// - Apply evolves the state from an event, without any rule (a past fact cannot be refused).
/// Loading an account = replaying Apply over its whole history.
/// </summary>
public class Account
{
    // …
    public void Withdraw(decimal amount, string reason, DateTime date)
    {
        CheckOpen();
        CheckAmount(amount);
        if (amount > Balance)
            throw new BusinessRuleException("Insufficient balance.");
        Record(new MoneyWithdrawn(Id, amount, reason, date));
    }
    // …

    // ─── State evolution ───

    private void Record(IEvent @event)
    {
        Apply(@event);
        _new.Add(@event);
    }

    private void Apply(IEvent @event)
    {
        switch (@event)
        {
            case AccountOpened e:
                (Id, Owner, Balance) = (e.AccountId, e.Owner, e.InitialDeposit);
                break;
            case MoneyDeposited e:
                Balance += e.Amount;
                break;
            case MoneyWithdrawn e:
                Balance -= e.Amount;
                break;
            case TransferSent e:
                Balance -= e.Amount;
                break;
            case TransferReceived e:
                Balance += e.Amount;
                break;
            case AccountClosed:
                Closed = true;
                break;
        }
        Version++;
    }
```

Loading an account means replaying `Appliquer` over its whole history:

```csharp
public static Account Rehydrate(IEnumerable<IEvent> history) =>
    FromSnapshot(null, history);
```

```mermaid
stateDiagram-v2
    [*] --> Ouvert : CompteOuvert
    Ouvert --> Ouvert : ArgentDepose / ArgentRetire / VirementEmis / VirementRecu
    Ouvert --> Cloture : CompteCloture (only if balance = 0)
    Cloture --> [*]
    note right of Cloture : any new operation is refused
```

### 2. The store: append only and optimistic concurrency

It appends the events of one or more streams, all or nothing; if a stream has moved since it was loaded, nothing is
written.
([MagasinEvenements.cs](../../10-EventSourcing/Es.Stockage/MagasinEvenements.cs))

```csharp
/// Appends the events of one or more streams, all or nothing.
/// Optimistic concurrency: if a stream moved since it was loaded, nothing is written.
public Task Append(params ToWrite[] writes)
{
    lock (_lock)
    {
        foreach (var write in writes)
        {
            var current = _events.Count(e => e.Stream == write.Stream);
            if (current != write.ExpectedVersion)
                throw new ConcurrencyConflictException(write.Stream, write.ExpectedVersion, current);
        }
        // … then add everything, in memory and in the .jsonl file
    }
    return Task.CompletedTask;
}
```

```mermaid
sequenceDiagram
    actor A as Teller A
    actor B as Teller B
    participant API as Es.Api
    participant M as Store
    A->>API: GET /comptes/alice → version 1
    B->>API: GET /comptes/alice → version 1
    A->>API: withdraw €80 (If-Match: 1)
    API->>M: Ajouter(alice, versionAttendue: 1)
    M-->>API: ok → version 2
    B->>API: withdraw €80 (If-Match: 1)
    API-->>B: 409 Conflict "expected version 1, current version 2"
    Note over B: B reloads the account and decides<br/>with up-to-date data
```

### 3. Snapshots: not replaying everything

Every 5 events, a picture of the state is **cached**. It can be erased without losing anything.
([DepotComptes.cs](../../10-EventSourcing/Es.Stockage/DepotComptes.cs))

```csharp
public async Task<Account> Load(Guid id)
{
    var snapshot = snapshots.Latest(id);
    var rest = await store.ReadStream(id, afterVersion: snapshot?.Version ?? 0);
    if (snapshot is null && rest.Count == 0)
        throw new AccountNotFoundException(id);
    return Account.FromSnapshot(snapshot, rest.Select(e => e.Event()));
}
```

### 4. Time travel

The account's state as it was at a given version:

```csharp
/// <summary>Time travel: the state of the account as it was at a given version.</summary>
public async Task<Account> LoadAtVersion(Guid id, int version)
{
    var history = await store.ReadStream(id);
    if (history.Count == 0)
        throw new AccountNotFoundException(id);
    return Account.Rehydrate(history.Where(e => e.Version <= version).Select(e => e.Event()));
}
```

`GET /comptes/{id}/etat?version=2` or `?date=…`; `GET /comptes/{id}/evenements` gives the full audit.

### 5. Evolving an event without rewriting history (*upcasting*)

`ArgentRetire` gained a `Motif` (reason) field in v2. The old v1 events stay untouched; they are converted **on read**
(reason "Non précisé", i.e. unspecified).
([SerialiseurEvenements.cs](../../10-EventSourcing/Es.Stockage/SerialiseurEvenements.cs))

```csharp
private static readonly Dictionary<string, Func<string, (string Type, string Json)>> Upcasters = new()
{
    // v1: { accountId, amount, date }  →  v2: + reason
    ["MoneyWithdrawn"] = json =>
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node["reason"] = "Not specified";
        return ("MoneyWithdrawn.v2", node.ToJsonString());
    }
};
```

### 6. A projection added after the fact knows the whole past

Since it is built by replaying the whole stream, it immediately knows every past withdrawal, including those made
before it existed — impossible with a classic database, which only kept the final state.
([Projections.cs](../../10-EventSourcing/Es.Projections/Projections.cs))

```csharp
// ─── Largest withdrawals: a projection added AFTERWARDS ───
// Since it is built by replaying the whole stream, it knows all the past withdrawals right away,
// including those made before it existed. Impossible with a classic database, which kept only the final state.
public class LargestWithdrawalsProjection : IProjection { /* … */ }
```

`POST /admin/projections/reconstruire` erases all the views and replays the whole store.

---

## The tests: Given / When / Then

Event Sourcing's natural style: *given* some facts, *when* a command arrives, *then* these new facts are expected.
(In the code: *EtantDonne* = given, *Quand* = when, *Alors* = then.)
([CompteTests.cs](../../10-EventSourcing/Es.Tests/CompteTests.cs))

```csharp
private static IReadOnlyList<IEvent> When(IEvent[] given, Action<Account> command)
{
    var account = Account.Rehydrate(given);
    command(account);
    return account.NewEvents;
}

[Fact]
public void A_possible_withdrawal_produces_MoneyWithdrawn()
{
    var then = When(AccountWith100Euros, c => c.Withdraw(30m, "Groceries", T));

    Assert.Equal([new MoneyWithdrawn(Id, 30m, "Groceries", T)], then);
}
```

Other tests ([StockageTests.cs](../../10-EventSourcing/Es.Tests/StockageTests.cs), [ApiTests.cs](../../10-EventSourcing/Es.Tests/ApiTests.cs)):
concurrency conflict, all-or-nothing transfer across two streams, restart from the file, upcasting, snapshots
(same result, fewer events read), time travel, projection rebuild.

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| **Full**, immutable **audit**, by construction | Nothing is changed or deleted: mistakes are corrected with a compensating event |
| **Time travel**: the state at any date | **GDPR**: plan for erasure (encrypt personal data, then destroy the key) |
| New **retroactive** views over the whole history | Events to **version** forever (upcasters) |
| Explicit concurrency (versions); Given/When/Then tests | Projections to maintain; a real learning curve |

**When to use it**: when history is itself business data (banking, accounting, logistics, insurance, regulatory
audit) — and **only in those modules**. For CRUD, it is overkill. In production: EventStoreDB, Marten (PostgreSQL),
Axon. Almost always combined with [CQRS](09-cqrs.md) for reads.
