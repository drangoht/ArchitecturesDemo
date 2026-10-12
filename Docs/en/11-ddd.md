# 11. Domain-Driven Design (DDD)

> **In one sentence**: start from the business, speak **its** language, and let the model drive the code — inside a
> context (the **tactical** side) as well as between contexts (the **strategic** side).

📁 Code: [`11-DDD`](../../11-DDD) · ⬅️ [Event Sourcing](10-event-sourcing.md) · ➡️ [Serverless](12-serverless.md) ·
🌐 [Français](../11-ddd.md)

---

## The idea

Formalized by **Eric Evans** in *Domain-Driven Design* (2003), the "blue book". DDD is **not a folder layout** like the
previous demos: it is a **design approach**, usable with a hexagonal, Onion or Clean architecture (the demo uses an
Onion base). It has two sides:

```mermaid
flowchart TB
    DDD["Domain-Driven Design"]
    DDD --> S["<b>Strategic</b><br/>split and organize"]
    DDD --> T["<b>Tactical</b><br/>model well inside a context"]
    S --> S1["Bounded contexts"]
    S --> S2["Context map"]
    S --> S3["Published language /<br/>open host service"]
    S --> S4["Anti-corruption layer"]
    T --> T0["Ubiquitous language"]
    T --> T1["Value objects"]
    T --> T2["Entities / Aggregates"]
    T --> T3["Factories"]
    T --> T4["Domain services"]
    T --> T5["Repositories"]
    T --> T6["Domain events"]
```

---

## The strategic side: the context map

Two **bounded contexts** model **the same reality differently** (Account Keeping and Compliance):

| | "Account Keeping" context | "Compliance" context |
|---|---|---|
| An account is… | a balance, a limit, movements | a **due diligence case**: observed transfers and alerts |
| Currency | the account's currency (EUR, USD, CHF) | **always in euros** |
| Concern | executing operations correctly | detecting money laundering and structuring |
| Glossary | [LANGUAGE.md](../../11-DDD/Ddd.AccountKeeping.Domain/LANGUAGE.md) | [LANGUAGE.md](../../11-DDD/Ddd.Compliance/LANGUAGE.md) |

A single "Account" model for everyone would be **bloated**: each team would add its own fields.

```mermaid
flowchart TB
    H["LEGACY exchange system<br/>'EURUSD=10832;…'"]
    subgraph TDC["Account Keeping"]
        ACL["Anti-corruption layer<br/>ExchangeRateAdapter"]
        Dom["Domain<br/>Account, Money, Iban…"]
        PL["Published language<br/>TransferSent · IAccountKeepingApi"]
        ACL --> Dom --> PL
    end
    subgraph CONF["Compliance"]
        Surv["TransferMonitoring<br/>(translation)"]
        Dos["VigilanceFile · Alert"]
        Surv --> Dos
    end
    H -->|"upstream"| ACL
    PL -->|"TransferSent<br/>(event)"| Surv
    Surv -->|"BlockAccount<br/>(open host service)"| PL

    classDef ext fill:#f6f8fa,stroke:#8c959f,stroke-dasharray: 5 5
    class H ext
```

| Relationship | In the demo | Why |
|---|---|---|
| **Anti-corruption layer** | `ExchangeRateAdapter` in front of the legacy exchange system | Its format (rate × 10,000, pairs from the euro, unknown currencies) must not pollute our model |
| **Published language** | `Ddd.AccountKeeping.Contracts`: `TransferSent` in simple types | The other contexts don't see `Money`, `Iban`…: the internal model can evolve |
| **Open host service** | `IAccountKeepingApi.BlockAccount` | Compliance **requests** a block; Account Keeping is the one that executes it |
| **Customer / supplier** | `AmountInEuros` added to `TransferSent` | The downstream context (Compliance) expressed a need to the upstream context |

---

## The tactical side: the Account Keeping model

```mermaid
classDiagram
    direction LR
    class Account {
        <<Aggregate root>>
        +AccountId Id
        +Iban Iban
        +OwnerName Owner
        +Money Balance
        +Money DailyWithdrawalLimit
        +AccountStatus Status
        +Withdraw(Money, DateTime)
        +Block(string, DateTime)
    }
    class Movement {
        <<Internal entity>>
        +Guid Id
        +MovementType Type
        +Money Amount
        +string Label
    }
    class Money {
        <<Value object>>
        +decimal Amount
        +Currency Currency
        +operator +(Money, Money)
    }
    class Iban {
        <<Value object>>
        +string Value
        +From(string)$
        +Generate(pays, bban)$
    }
    class AccountFactory {
        <<Factory>>
        +Open(...) Account
    }
    class TransferService {
        <<Domain service>>
        +Transfer(source, cible, montant, date)
    }
    class IAccounts {
        <<Repository>>
        +ById(AccountId)
        +ByIban(Iban)
    }
    class ICurrencyConverter {
        <<Port>>
        +Convert(Money, Currency)
    }
    Account "1" *-- "*" Movement : contains
    Account --> Money
    Account --> Iban
    AccountFactory ..> Account : creates
    AccountFactory ..> IAccounts : numbers
    TransferService ..> Account : debits / credits
    TransferService ..> ICurrencyConverter
```

The code is quoted exactly as it is.

### 1. The ubiquitous language

Each glossary term exists as is in the code: if an expert says *plafond de retrait journalier* (daily withdrawal limit),
you find `DailyWithdrawalLimit`, not `MaxDailyLimit`.

### 2. Always-valid value objects

An invalid `Iban` **cannot exist**: receiving an `Iban` is enough to know it is valid.
([Iban.cs](../../11-DDD/Ddd.AccountKeeping.Domain/ValueObjects/Iban.cs))

```csharp
public static Iban From(string input)
{
    var value = new string((input ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
    // … format check
    if (Modulo97(value[4..] + value[..4]) != 1)
        throw new BusinessRuleException($"Invalid IBAN (wrong check digits): \"{input}\".");
    return new Iban(value);
}
```

`Money` (money) carries its currency and **refuses to mix** euros and dollars — even in a comparison: it is a domain
rule, carried by the type itself.
([Money.cs](../../11-DDD/Ddd.AccountKeeping.Domain/ValueObjects/Money.cs))

```csharp
// The operations refuse to mix currencies: it is a domain rule, carried by the type itself.
public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, SameCurrency(a, b));
public static bool operator >(Money a, Money b) => SameCurrency(a, b) is not null && a.Amount > b.Amount;

private static Currency SameCurrency(Money a, Money b) =>
    a.Currency == b.Currency ? a.Currency
        : throw new BusinessRuleException($"Cannot combine {a.Currency} and {b.Currency} without conversion.");
```

Compare with the bare `decimal` and `string` of the other demos (*primitive obsession*).

### 3. An aggregate whose boundary is dictated by an invariant

To check the **daily** withdrawal limit, the account must know its movements of the day: it is this rule that sets the
aggregate's **boundary**. A movement is never loaded or saved alone.
([Account.cs](../../11-DDD/Ddd.AccountKeeping.Domain/Accounts/Account.cs))

```csharp
public void Withdraw(Money amount, DateTime date)
{
    CheckDebitPossible(amount);

    var alreadyWithdrawn = WithdrawalsOfTheDay(date);
    if (alreadyWithdrawn + amount > DailyWithdrawalLimit)
        throw new BusinessRuleException(
            $"Daily withdrawal limit of {DailyWithdrawalLimit} exceeded (already withdrawn today: {alreadyWithdrawn}).");

    Balance -= amount;
    _movements.Add(new Movement(date, MovementType.Withdrawal, amount, "Withdrawal"));
    Raise(new WithdrawalMade(Id, amount, date));
}
```

### 4. Factory and domain service

The **factory** computes the number and the IBAN ([Factory.cs](../../11-DDD/Ddd.AccountKeeping.Domain/Accounts/Factory.cs));
the **domain service** carries the transfer, which touches two aggregates and an exchange rate
([TransferService.cs](../../11-DDD/Ddd.AccountKeeping.Domain/Accounts/TransferService.cs)):

```csharp
var creditedAmount = target.Currency == amount.Currency ? amount : await converter.Convert(amount, target.Currency);

source.Debit(amount, $"Transfer to {target.Owner}", date);
target.Credit(creditedAmount, $"Transfer from {source.Owner}", date);

source.Report(new TransferMade(Guid.NewGuid(), source.Id, source.Iban, source.Owner,
    target.Iban, amount, creditedAmount, date));
```

### 5. The anti-corruption layer

It translates the legacy system's model into our language (`Money`, `Currency`); since the legacy system only quotes
from the euro, every cross rate (USD → CHF…) goes through the euro.
([AntiCorruptionLayer.cs](../../11-DDD/Ddd.AccountKeeping.Infrastructure/AntiCorruptionLayer.cs))

```csharp
public class LegacyQuoteSystem
{
    public virtual string TodaysQuotes() => "EURUSD=10832;EURCHF=9415;EURGBP=8571;EURJPY=1612300";
}

/// ANTI-CORRUPTION LAYER: it translates the model of the legacy system into our language (Money, Currency).
public class ExchangeRateAdapter(LegacyQuoteSystem legacySystem) : ICurrencyConverter
{
    public Task<Money> Convert(Money amount, Currency currency)
    {
        var ratesFromEuro = ReadQuotes(legacySystem.TodaysQuotes());

        // The legacy system only quotes from the euro: any cross rate goes through the euro (USD → CHF…).
        var amountInEuros = amount.Amount / ratesFromEuro[amount.Currency];
        var converted = decimal.Round(amountInEuros * ratesFromEuro[currency], 2, MidpointRounding.ToEven);
        return Task.FromResult(new Money(converted, currency));
    }
}
```

### 6. Translating domain events into the published language

([Integration.cs](../../11-DDD/Ddd.AccountKeeping.Application/Integration.cs))

```csharp
var integration = new TransferSent(
    v.TransferId, v.Source.Value, v.SourceIban.Value, v.SourceOwner.Value, v.TargetIban.Value,
    v.DebitedAmount.Amount, v.DebitedAmount.Currency.Code, inEuros.Amount, v.Date);

foreach (var subscriber in subscribers())
    await subscriber.OnTransferSent(integration);
```

### The structuring scenario: two contexts cooperate

```mermaid
sequenceDiagram
    actor Bob
    participant SC as AccountsService (Account Keeping)
    participant C as Bob's account (aggregate)
    participant Pub as EventPublisher
    participant Surv as TransferMonitoring (Compliance)
    participant Dos as VigilanceFile
    participant Api as IAccountKeepingApi
    loop 3 transfers of €10 within a few seconds
        Bob->>SC: Transfer(ibanAlice, 10)
        SC->>C: Debit (via TransferService)
        SC->>SC: Validate (save)
        SC->>Pub: TransferMade (domain event)
        Pub->>Surv: TransferSent (published language, in euros)
        Surv->>Dos: Observe(ObservedTransfer)
    end
    Dos-->>Surv: Critical alert "structuring suspected"
    Surv->>Api: BlockAccount(bob, reason)
    Api->>C: Block(reason)
    Bob->>SC: 4th transfer
    SC-->>Bob: 400 "This account is blocked (…)"
```

---

## The tests

- **Value objects** ([ValueObjectsTests.cs](../../11-DDD/Ddd.Tests/ValueObjectsTests.cs)): equality by value,
  currencies that can't be mixed, invalid amounts and IBANs impossible to create.
- **Domain** ([DomainTests.cs](../../11-DDD/Ddd.Tests/DomainTests.cs)): daily limit, blocked account, conversion
  through the anti-corruption layer (€100 → $108.32; $108.32 → CHF 94.15 through a cross rate), Compliance rules.
- **Context boundaries** ([ApiTests.cs](../../11-DDD/Ddd.Tests/ApiTests.cs)):

```csharp
[Fact]
public void Compliance_only_knows_the_published_language_of_account_keeping()
{
    Assert.Equal(["Ddd.AccountKeeping.Contracts"], InternalDependencies(typeof(VigilanceFile).Assembly));
}
```

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| Code readable by domain experts (same vocabulary) | Requires **available experts** to build the language |
| Value objects: errors impossible by construction | A lot of ceremony for CRUD |
| Bounded contexts: focused models, autonomous teams | Splitting contexts is hard, and expensive to fix |
| Tells **where to cut** modules and services | Often wrongly reduced to its tactical toolbox |

**DDD has no column in the comparison**: it is not a competing architecture, but the approach that says **where to
cut**. Its bounded contexts give the modules ([demo 7](07-monolithe-modulaire.md)) or the services
([demo 8](08-microservices.md)), and its aggregates give the event streams ([demo 10](10-event-sourcing.md)).
