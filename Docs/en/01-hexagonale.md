# 1. Hexagonal Architecture (Ports & Adapters)

**French name**: *Architecture hexagonale*

> **In one sentence**: the business is at the center and depends on nothing; the outside world plugs into it through
> **ports** (interfaces) and interchangeable **adapters** (implementations).

📁 Code: [`01-Hexagonale`](../../01-Hexagonale) · ⬅️ [Layered](00-couches.md) · ➡️ [Onion](02-onion.md) ·
🌐 [Français](../01-hexagonale.md)

---

## The idea

Proposed by **Alistair Cockburn** in 2005. The observation: in a layered architecture, the business ends up depending
on the database and on the user interface. Cockburn suggests reasoning in terms of **inside / outside** rather than
top / bottom. There is nothing magical about the hexagon: it is just a shape that leaves room to draw several ports.

```mermaid
flowchart LR
    subgraph Entrants["Inbound adapters"]
        Web["Hexa.Adapters.Web<br/>HTTP API"]
        Cli["Hexa.Adapters.Cli<br/>command line"]
        Tests["Tests"]
    end

    subgraph Hexagone["Hexagon"]
        direction TB
        PE["Inbound ports<br/>IConsulterCompte<br/>IRetirerArgent<br/>IEffectuerVirement"]
        App["Application<br/>ConsulterCompte, RetirerArgent,<br/>EffectuerVirement"]
        Dom["Domain<br/>Compte"]
        PS["Outbound ports<br/>IComptes<br/>INotifications"]
        PE --> App --> Dom
        App --> PS
    end

    subgraph Sortants["Outbound adapters"]
        Mem["ComptesEnMemoire"]
        Json["ComptesFichierJson"]
        Notif["NotificationsJournal"]
    end

    Web --> PE
    Cli --> PE
    Tests --> PE
    Mem -. "implements" .-> PS
    Json -. "implements" .-> PS
    Notif -. "implements" .-> PS

    classDef hexa fill:#fff8c5,stroke:#9a6700
    class Hexagone hexa
```

Two kinds of ports:

| | **Inbound** port (*driving*, primary) | **Outbound** port (*driven*, secondary) |
|---|---|---|
| Role | What the application **offers** | What the application **needs** |
| Who calls it? | An inbound adapter (API, CLI, test) | The application itself |
| Who implements it? | The application (`RetirerArgent`) | An outbound adapter (`ComptesFichierJson`) |
| Example | `IRetirerArgent` (withdraw money) | `IComptes` (accounts), `INotifications` |

**Every dependency arrow points toward the hexagon**, which references no other project.

---

## Solution layout

| Project | Role |
|---|---|
| `Hexa.Hexagone` | Domain + ports + use cases. **No references**, neither project nor package. |
| `Hexa.Adapters.Persistance` | Two adapters of the **same** port `IComptes`: in memory and JSON file |
| `Hexa.Adapters.Notifications` | Adapter of the `INotifications` port (text log) |
| `Hexa.Adapters.Web` | Inbound HTTP adapter + *composition root* (wires everything) |
| `Hexa.Adapters.Cli` | Second inbound adapter: the command line |
| `Hexa.Tests` | Tests of the hexagon, adapter **contract tests**, architecture test |

---

## The code, step by step

The code is quoted exactly as it is, French comments included.

### 1. The ports: interfaces inside the hexagon

([PortsEntrants.cs](../../01-Hexagonale/Hexa.Hexagone/Ports/Entrants/PortsEntrants.cs),
[PortsSortants.cs](../../01-Hexagonale/Hexa.Hexagone/Ports/Sortants/PortsSortants.cs))

```csharp
// Inbound ports ("driving", primary): what the application OFFERS to the outside world.
public interface IWithdrawMoney
{
    Task Execute(Guid accountId, decimal amount);
}

// Outbound ports ("driven", secondary): what the application NEEDS.
public interface IAccounts
{
    Task<Account?> Load(Guid id);
    Task Save(Account account);
}

public interface INotifications
{
    Task WithdrawalMade(Guid accountId, decimal amount);
    Task TransferMade(Guid sourceId, Guid targetId, decimal amount);
}
```

### 2. A use case: an inbound port implemented with outbound ports

([CasUtilisation.cs](../../01-Hexagonale/Hexa.Hexagone/Application/CasUtilisation.cs))

```csharp
public class WithdrawMoney(IAccounts accounts, INotifications notifications) : IWithdrawMoney
{
    public async Task Execute(Guid accountId, decimal amount)
    {
        var account = await accounts.Load(accountId) ?? throw new AccountNotFoundException(accountId);
        account.Withdraw(amount);
        await accounts.Save(account);
        await notifications.WithdrawalMade(accountId, amount);
    }
}
```

The hexagon doesn't know whether it is called over HTTP or from the command line, nor whether the accounts are in
memory or in a file.

### 3. Two adapters for the same port

The storage format belongs to the adapter: the domain doesn't know it is serialized.
([ComptesFichierJson.cs](../../01-Hexagonale/Hexa.Adapters.Persistance/ComptesFichierJson.cs))

```csharp
public class JsonFileAccounts : IAccounts
{
    // …
    // Storage format specific to the adapter: the domain does not know it is serialized.
    private record Row(Guid Id, string Owner, decimal Balance)
    {
        public static Row From(Account account) => new(account.Id, account.Owner, account.Balance);
        public Account ToAccount() => new(Id, Owner, Balance);
    }
}
```

### 4. The *composition root* picks the adapters

It is **the only place** that knows both the hexagon and the adapters. Outbound adapters can be swapped by
configuration: `--Persistance=Json` or `Memoire` (default).
([Program.cs](../../01-Hexagonale/Hexa.Adapters.Web/Program.cs))

```csharp
// Outbound adapters, swappable by configuration: --Persistence=Json or Memory (default).
builder.Services.AddSingleton<IAccounts>(_ => builder.Configuration["Persistence"] switch
{
    "Json" => new JsonFileAccounts(builder.Configuration["AccountsFile"] ?? "accounts.json", DemoData.Accounts()),
    _ => new InMemoryAccounts(DemoData.Accounts())
});
builder.Services.AddSingleton<INotifications>(new LogNotifications(Console.Out));

// Inbound ports → their implementation in the hexagon.
builder.Services.AddScoped<IWithdrawMoney, WithdrawMoney>();
```

The CLI does the same **without a dependency injection container**: the hexagon doesn't need one.
([Hexa.Adapters.Cli/Program.cs](../../01-Hexagonale/Hexa.Adapters.Cli/Program.cs))

```csharp
IAccounts accounts = new JsonFileAccounts("accounts.json", DemoData.Accounts());
INotifications notifications = new LogNotifications(Console.Out);
IWithdrawMoney withdraw = new WithdrawMoney(accounts, notifications);
```

### 5. The HTTP adapter translates, without any rules

Translating business errors into HTTP status codes is the adapter's job, not the hexagon's.
([ComptesEndpoints.cs](../../01-Hexagonale/Hexa.Adapters.Web/ComptesEndpoints.cs))

```csharp
// Translating business errors into HTTP codes is the adapter's job, not the hexagon's.
private static async Task<IResult> Translate(Func<Task<IResult>> action)
{
    try { return await action(); }
    catch (AccountNotFoundException e) { return Results.NotFound(new { error = e.Message }); }
    catch (BusinessRuleException e) { return Results.BadRequest(new { error = e.Message }); }
}
```

### Two entry points, one hexagon

```mermaid
sequenceDiagram
    actor U1 as Browser
    actor U2 as Terminal
    participant W as Web adapter
    participant C as CLI adapter
    participant H as RetirerArgent (hexagon)
    participant P as IComptes → ComptesFichierJson
    participant N as INotifications → NotificationsJournal
    U1->>W: POST /comptes/{id}/retrait
    W->>H: Executer(id, 100)
    H->>P: Charger / Sauvegarder
    H->>N: RetraitEffectue
    W-->>U1: 204
    U2->>C: retirer {id} 100
    C->>H: Executer(id, 100)
    H->>P: Charger / Sauvegarder
    H->>N: RetraitEffectue
    C-->>U2: "Retrait effectué."
    Note over P: Started from 01-Hexagonale, the API (--Persistance=Json)<br/>and the CLI share the same comptes.json
```

---

## The tests

**Tests of the hexagon**: test adapters are plugged into the ports; no web server, no database.
([CasUtilisationTests.cs](../../01-Hexagonale/Hexa.Tests/Hexagone/CasUtilisationTests.cs))

```csharp
[Fact]
public async Task A_valid_withdrawal_debits_the_account_and_notifies()
{
    await new WithdrawMoney(_accounts, _notifications).Execute(Alice, 30m);

    Assert.Equal(70m, (await _accounts.Load(Alice))!.Balance);
    Assert.Equal([$"withdrawal 30 {Alice}"], _notifications.Sent);
}
```

**Contract tests**: the same tests run against **every** adapter of the `IComptes` port. This is what guarantees that
one can replace the other without surprises.
([ContratIComptesTests.cs](../../01-Hexagonale/Hexa.Tests/Adapters/ContratIComptesTests.cs))

```csharp
public abstract class IAccountsContract
{
    protected abstract IAccounts Create(params Account[] initialAccounts);

    [Fact]
    public async Task An_unsaved_change_is_not_persisted() { /* … */ }
}

public class InMemoryAccountsTests : IAccountsContract
{
    protected override IAccounts Create(params Account[] initialAccounts) => new InMemoryAccounts(initialAccounts);
}

public class JsonFileAccountsTests : IAccountsContract, IDisposable { /* same tests, on a temporary file */ }
```

```mermaid
flowchart LR
    T["ContratIComptes<br/>(3 abstract tests)"]
    T --> A["ComptesEnMemoireTests"]
    T --> B["ComptesFichierJsonTests"]
    A --> X["ComptesEnMemoire"]
    B --> Y["ComptesFichierJson"]
```

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| The business depends on nothing: testable without infrastructure | One interface per external need: more code |
| Interchangeable adapters (memory, file, SQL…) | Risk of pass-through ports that add nothing |
| Several entry points into the same business | The center is not structured: to be completed (Onion, DDD) |
| Adapter contract tests | |

**When to use it**: rich business logic, infrastructure likely to change, need for several channels (API, CLI,
messages). **Variants**: [Onion](02-onion.md) structures the center in layers; [Clean](03-clean.md) formalizes the
use cases. All three share the same rule: *dependencies point toward the business*.
