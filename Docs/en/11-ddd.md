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

Two **bounded contexts** model **the same reality differently** (*Tenue de compte* = Account Keeping, *Conformité* =
Compliance):

| | "Account Keeping" context | "Compliance" context |
|---|---|---|
| An account is… | a balance, a limit, movements | a **due diligence case**: observed transfers and alerts |
| Currency | the account's currency (EUR, USD, CHF) | **always in euros** |
| Concern | executing operations correctly | detecting money laundering and structuring |
| Glossary | [LANGAGE.md](../../11-DDD/Ddd.TenueDeCompte.Domaine/LANGAGE.md) | [LANGAGE.md](../../11-DDD/Ddd.Conformite/LANGAGE.md) |

A single "Account" model for everyone would be **bloated**: each team would add its own fields.

```mermaid
flowchart TB
    H["LEGACY exchange system<br/>'EURUSD=10832;…'"]
    subgraph TDC["Account Keeping"]
        ACL["Anti-corruption layer<br/>AdaptateurTauxDeChange"]
        Dom["Domain<br/>Compte, Argent, Iban…"]
        PL["Published language<br/>VirementEmis · ITenueDeCompteApi"]
        ACL --> Dom --> PL
    end
    subgraph CONF["Compliance"]
        Surv["SurveillanceDesVirements<br/>(translation)"]
        Dos["DossierDeVigilance · Alerte"]
        Surv --> Dos
    end
    H -->|"upstream"| ACL
    PL -->|"VirementEmis<br/>(event)"| Surv
    Surv -->|"BloquerCompte<br/>(open host service)"| PL

    classDef ext fill:#f6f8fa,stroke:#8c959f,stroke-dasharray: 5 5
    class H ext
```

| Relationship | In the demo | Why |
|---|---|---|
| **Anti-corruption layer** | `AdaptateurTauxDeChange` in front of the legacy exchange system | Its format (rate × 10,000, pairs from the euro, unknown currencies) must not pollute our model |
| **Published language** | `Ddd.TenueDeCompte.Contrats`: `VirementEmis` in simple types | The other contexts don't see `Argent`, `Iban`…: the internal model can evolve |
| **Open host service** | `ITenueDeCompteApi.BloquerCompte` | Compliance **requests** a block; Account Keeping is the one that executes it |
| **Customer / supplier** | `MontantEnEuros` added to `VirementEmis` | The downstream context (Compliance) expressed a need to the upstream context |

---

## The tactical side: the Account Keeping model

```mermaid
classDiagram
    direction LR
    class Compte {
        <<Aggregate root>>
        +CompteId Id
        +Iban Iban
        +NomTitulaire Titulaire
        +Argent Solde
        +Argent PlafondRetraitJournalier
        +StatutCompte Statut
        +Retirer(Argent, DateTime)
        +Bloquer(string, DateTime)
    }
    class Mouvement {
        <<Internal entity>>
        +Guid Id
        +TypeMouvement Type
        +Argent Montant
        +string Libelle
    }
    class Argent {
        <<Value object>>
        +decimal Montant
        +Devise Devise
        +operator +(Argent, Argent)
    }
    class Iban {
        <<Value object>>
        +string Valeur
        +Depuis(string)$
        +Generer(pays, bban)$
    }
    class FabriqueDeComptes {
        <<Factory>>
        +Ouvrir(...) Compte
    }
    class ServiceDeVirement {
        <<Domain service>>
        +Virer(source, cible, montant, date)
    }
    class IComptes {
        <<Repository>>
        +ParId(CompteId)
        +ParIban(Iban)
    }
    class IConvertisseurDevises {
        <<Port>>
        +Convertir(Argent, Devise)
    }
    Compte "1" *-- "*" Mouvement : contains
    Compte --> Argent
    Compte --> Iban
    FabriqueDeComptes ..> Compte : creates
    FabriqueDeComptes ..> IComptes : numbers
    ServiceDeVirement ..> Compte : debits / credits
    ServiceDeVirement ..> IConvertisseurDevises
```

The code is quoted exactly as it is, French comments and messages included.

### 1. The ubiquitous language

Each glossary term exists as is in the code: if an expert says *plafond de retrait journalier* (daily withdrawal limit),
you find `PlafondRetraitJournalier`, not `MaxDailyLimit`. (Here the business language is French, so the code is too.)

### 2. Always-valid value objects

An invalid `Iban` **cannot exist**: receiving an `Iban` is enough to know it is valid.
([Iban.cs](../../11-DDD/Ddd.TenueDeCompte.Domaine/ObjetsValeurs/Iban.cs))

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

`Argent` (money) carries its currency and **refuses to mix** euros and dollars — even in a comparison: it is a domain
rule, carried by the type itself.
([Argent.cs](../../11-DDD/Ddd.TenueDeCompte.Domaine/ObjetsValeurs/Argent.cs))

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
([Compte.cs](../../11-DDD/Ddd.TenueDeCompte.Domaine/Comptes/Compte.cs))

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

The **factory** computes the number and the IBAN ([Fabrique.cs](../../11-DDD/Ddd.TenueDeCompte.Domaine/Comptes/Fabrique.cs));
the **domain service** carries the transfer, which touches two aggregates and an exchange rate
([ServiceDeVirement.cs](../../11-DDD/Ddd.TenueDeCompte.Domaine/Comptes/ServiceDeVirement.cs)):

```csharp
var creditedAmount = target.Currency == amount.Currency ? amount : await converter.Convert(amount, target.Currency);

source.Debit(amount, $"Transfer to {target.Owner}", date);
target.Credit(creditedAmount, $"Transfer from {source.Owner}", date);

source.Report(new TransferMade(Guid.NewGuid(), source.Id, source.Iban, source.Owner,
    target.Iban, amount, creditedAmount, date));
```

### 5. The anti-corruption layer

It translates the legacy system's model into our language (`Argent`, `Devise`); since the legacy system only quotes
from the euro, every cross rate (USD → CHF…) goes through the euro.
([CoucheAntiCorruption.cs](../../11-DDD/Ddd.TenueDeCompte.Infrastructure/CoucheAntiCorruption.cs))

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

([Integration.cs](../../11-DDD/Ddd.TenueDeCompte.Application/Integration.cs))

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
    participant SC as ServiceComptes (Account Keeping)
    participant C as Bob's account (aggregate)
    participant Pub as PublicateurEvenements
    participant Surv as SurveillanceDesVirements (Compliance)
    participant Dos as DossierDeVigilance
    participant Api as ITenueDeCompteApi
    loop 3 transfers of €10 within a few seconds
        Bob->>SC: Virer(ibanAlice, 10)
        SC->>C: Debiter (via ServiceDeVirement)
        SC->>SC: Valider (save)
        SC->>Pub: VirementEffectue (domain event)
        Pub->>Surv: VirementEmis (published language, in euros)
        Surv->>Dos: Observer(VirementObserve)
    end
    Dos-->>Surv: Critical alert "suspected structuring"
    Surv->>Api: BloquerCompte(bob, motif)
    Api->>C: Bloquer(motif)
    Bob->>SC: 4th transfer
    SC-->>Bob: 400 "Ce compte est bloqué (…)"
```

---

## The tests

- **Value objects** ([ObjetsValeursTests.cs](../../11-DDD/Ddd.Tests/ObjetsValeursTests.cs)): equality by value,
  currencies that can't be mixed, invalid amounts and IBANs impossible to create.
- **Domain** ([DomaineTests.cs](../../11-DDD/Ddd.Tests/DomaineTests.cs)): daily limit, blocked account, conversion
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
