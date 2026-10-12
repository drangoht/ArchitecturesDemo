# 22. Strangler Fig (progressive migration)

> **In one sentence**: to replace an old system without rewriting everything at once, you put a **façade** in front of
> it, then take its features away **one route at a time** in favor of a new service, until the day it is no longer
> used and can be switched off.

📁 Code: [`22-StranglerFig`](../../22-StranglerFig) · ⬅️ [Choreography](21-choregraphie.md) · ➡️ [REST, gRPC, GraphQL](23-rest-grpc-graphql.md) ·
🌐 [Français](../22-strangler-fig.md)

---

## The idea

The strangler fig sprouts in a tree's branches, sends its roots down to the ground, gradually wraps its host and ends
up standing on its own once the tree is gone. Martin Fowler named this migration pattern after it (2004).

All the other demos build a **new** system. This one answers the question met far more often: **how do you move from an
existing system to one of these architectures?** The temptation is the complete rewrite, delivered in one go ("big
bang"): months without delivering anything, an old system that keeps evolving meanwhile, and a final switchover where
everything is decided in one night, with no way back.

```mermaid
flowchart LR
    subgraph E1["① START"]
        direction TB
        C1["Clients"] --> F1["Façade"] --> A1["Legacy<br/>(everything)"]
    end
    subgraph E2["② MIGRATION"]
        direction TB
        C2["Clients"] --> F2["Façade"]
        F2 -->|"statement, transfers"| A2["Legacy"]
        F2 -->|"lookup"| N2["New"]
        A2 <-.->|"synchronization"| N2
    end
    subgraph E3["③ END"]
        direction TB
        C3["Clients"] --> F3["Façade"] --> N3["New<br/>(everything)"]
    end
    E1 ~~~ E2 ~~~ E3
```

The Strangler Fig replaces that leap with a series of small steps: each migrated feature is **in production** right
away, on real traffic, and each step can be **undone** with one request. Clients never see the difference: they only
talk to the façade, whose address doesn't change.

---

## The projects

```mermaid
flowchart LR
    CL["Clients"] -->|"the historical contract"| FA["<b>Fig.Facade</b> :5122<br/>routing table<br/>shadow mode · control"]
    FA -->|"routes in<br/>Legacy or Shadow mode"| AN["<b>Fig.Legacy</b> :5240<br/>the system to replace"]
    FA -->|"routes in<br/>New mode (or Shadow)"| NO["<b>Fig.Modern</b> :5241<br/>its own domain<br/>+ anti-corruption layer"]
    NO -->|"① reads its log"| AN
    NO -->|"② carries over its writes"| AN
    NU["Nightly batch<br/>(fees)"] -->|"writes directly"| AN
```

| Project | Role |
|---|---|
| [Fig.Legacy](../../22-StranglerFig/Fig.Legacy) | The system to replace, written "the old way": one class that does everything, amounts in cents, dates without a time zone, uppercase labels truncated to 30 characters. It only received two internal routes for the migration (its change log, the import of the new system's operations) |
| [Fig.Modern](../../22-StranglerFig/Fig.Modern) | The replacement: its own model (euros as `decimal`, dates with a time zone), its own database, and the historical contract reproduced exactly |
| [Fig.Facade](../../22-StranglerFig/Fig.Facade) | The clients' only entry point: HTTP relay, routing table changeable live, shadow-mode comparisons, migration tracking. No business logic |

None of the three references the others: the façade and the new service only know the legacy over HTTP (architecture
test). The day the legacy is switched off, you delete its project and the anti-corruption layer; nothing else compiles
against it.

---

## 1. The façade: a routing table

Each route has a **mode**: who answers. It is changed live (`PUT /facade/routes/{name}`), without redeploying or restarting.

| Mode | Who answers | The new service… |
|---|---|---|
| `Legacy` (legacy) | the legacy | is not called |
| `Shadow` (shadow) | the legacy | is called **at the same time**, its response is compared then discarded |
| `Modern` (new) | the new one | answers; the legacy is no longer called for this route |

And above all: **anything undeclared goes to the legacy**. That is the pattern's starting point (an empty façade that
forwards everything), and the protection against what was never inventoried — as the comment says, what nobody took
over stays with the legacy. ([Relay.cs](../../22-StranglerFig/Fig.Facade/Relay.cs)) (The code is quoted exactly as
it is.)

```csharp
var route = table.Find(http.Request.Method, path);
var mode = route?.Mode ?? Mode.Legacy;   // what nobody took over stays with the legacy system: that is the starting point
```

Each response carries the header `X-Served-By: legacy` or `modern` (served by), and the façade counts, route by route,
how many calls each system served (`GET /facade/routes`). The `/internal/*` routes of both systems are never forwarded:
a client cannot read the legacy's log or trigger its fees through the façade.

The initial table, in `appsettings.json`, shows a migration in progress:

| Route | Method and path | Mode |
|---|---|---|
| `lookup` | `GET /accounts/{number}` | Modern |
| `operations` | `GET /accounts/{number}/operations` | Shadow |
| `statement` | `GET /accounts/{number}/statement` | Legacy |
| `transfers` | `POST /transfers` | Legacy |
| *(undeclared)* | `GET /branches`, and everything else | Legacy |

---

## 2. Shadow mode: prove before switching

The contract tests (section 4) check the cases someone thought of writing. Shadow mode checks **every case real traffic
produces**: the façade calls both systems, returns the legacy's response, and compares.

```mermaid
sequenceDiagram
    participant C as Client
    participant F as Façade
    participant A as Legacy
    participant N as New
    C->>F: GET /accounts/10001/operations
    par at the same time
        F->>A: GET /accounts/10001/operations
    and
        F->>N: GET /accounts/10001/operations
    end
    A-->>F: response
    F-->>C: the legacy's response, without waiting for the new one
    N-->>F: response (or nothing, beyond 500 ms)
    F->>F: compare: identical, divergence or abandoned
```

Both calls leave at the same time; the client only waits for the legacy:

```csharp
// Both calls leave at the same time; the client only waits for the legacy system.
var budget = new CancellationTokenSource(options.ShadowBudgetMs);
var shadow = Send("modern", Message(http.Request, body), budget.Token);
```

Compared AFTERWARDS, without being awaited: a slow or failing new service slows nobody down.

```csharp
// Compared AFTERWARDS, without being awaited: a slow or failed modern service slows nobody down.
_ = comparer.Compare(route!.Name, $"{http.Request.Method} {path}{http.Request.QueryString}", response, shadow, budget);
```

`GET /facade/comparisons` gives the summary: identical responses, **divergences** (with both bodies, to investigate)
and **abandoned** calls (the new service exceeded its budget or didn't answer; not a divergence, but worth watching
before handing it the load). The JSON is compared structurally: property order and whitespace don't matter, values do.

**Only a read has a shadow mode.** Executing a transfer on both sides would debit twice: the façade refuses (`400`) to
put a write in shadow mode. ([RoutingTable.cs](../../22-StranglerFig/Fig.Facade/RoutingTable.cs))

```csharp
// A write has no "shadow" mode: executing it on both sides would make two transfers.
private static void Check(Route route, Mode mode)
{
    if (mode == Mode.Shadow && route.Method != "GET")
```

For writes, confidence comes from the contract tests, then a careful switchover, reversible at any moment.

---

## 3. Two databases, a single truth: synchronizing both ways

During the migration, both systems have their own data, and both write: the legacy for its routes not yet migrated
**and for its internal processing** (here, the fees charged at night, which never go through the façade); the new one
for its migrated routes. So synchronization must go **both ways**.

```mermaid
sequenceDiagram
    participant N as New (Synchronization)
    participant A as Legacy
    Note over A: the nightly batch charges fees
    N->>A: ① GET /internal/log?since=7
    A-->>N: lines 8 and 9 (cents, dates without time zone)
    N->>N: translate (anti-corruption layer), apply,<br/>move the position to 9
    Note over N: a client makes a transfer,<br/>served by the new system
    N->>N: debit + credit + outbox (together)
    N->>A: ② POST /internal/imported-operations
    A-->>N: imported (idempotent)
    N->>N: empty the outbox
    N->>A: ① on the next cycle: the log contains the echo
    A-->>N: lines 10 and 11 (ORIG = MODERN, our identifiers)
    N->>N: operations already known: ignored
```

② Carrying over our writes: if the legacy doesn't answer, they stay in the outbox for the next cycle; since the import
is idempotent, a resent batch isn't counted twice.
([Synchronization.cs](../../22-StranglerFig/Fig.Modern/Synchronization.cs))

```csharp
// ② Forward our writes. If the legacy system does not answer, they stay in the outbox for the next
//    cycle; the import being idempotent, a batch sent again is not counted twice.
var toForward = store.ToForward();
if (toForward.Count > 0)
{
    await legacy.Import(toForward.Select(LegacyTranslator.ToLegacy).ToList(), cancellation);
    store.MarkForwarded(toForward);
}
```

The pitfalls of this two-way flow are those of any distributed system, and each has its test:

| Pitfall | Countermeasure | Test |
|---|---|---|
| A write from the new system comes back through the legacy's log (**loop**) | the echo carries the new system's identifier, already known: ignored | `The_echo_of_a_modern_transfer_does_not_come_back_twice` |
| The legacy is unreachable when carrying over | **outbox**: filled with the transfer, emptied only after confirmation | `If_the_legacy_system_is_unreachable_the_operations_wait_for_the_next_cycle` |
| The legacy imported, but the response was lost | **idempotent import**: the resent batch is counted only once | `A_batch_sent_again_after_a_lost_response_is_imported_only_once` |
| An unknown log line | synchronization **stops** rather than ignoring the line: a visible lag is better than a silent gap | `An_unknown_type_stops_the_translation_instead_of_being_ignored` |

This change log is a simplified version of **Change Data Capture** (Debezium on the database's transaction log, for
example). The first cycle rereads everything from the start: that is the initial data load.

### The anti-corruption layer

The legacy's log speaks its own language: six-letter columns (`ACCNUM`, `AMTCTS`, `OPEDAT`), cents, Paris time without
a time zone. A single class translates it, both ways, and the new system's model sees nothing of it (the same idea as
in the [DDD demo](11-ddd.md)). The legacy records Paris time without a time zone: this is where you decide which
instant it means. ([AntiCorruptionLayer.cs](../../22-StranglerFig/Fig.Modern/AntiCorruptionLayer.cs))

```csharp
/// <summary>The legacy system notes Paris time without time zone: this is where we decide which instant it is.</summary>
public static DateTimeOffset DateFromLegacy(string text)
{
    var local = DateTime.ParseExact(text, DateFormat, CultureInfo.InvariantCulture);
    return new DateTimeOffset(local, Paris.GetUtcOffset(local));
}
```

---

## 4. Reproducing the legacy contract, habits included

Clients depend on the legacy's API **as it is**, quirks included. The new service has its own model, but it exposes the
historical contract exactly: that is what allows switching a route without telling anyone.
([Contract.cs](../../22-StranglerFig/Fig.Modern/Contract.cs))

| The legacy's habit | What the new system must do |
|---|---|
| Computes in cents, then divides by 100: `947.1` in the JSON | never write `947.10`, even if the client sent `70.10` |
| Dates in Paris time, without a time zone: `2026-10-12T10:05:00` | convert from its `DateTimeOffset`; an operation on October 31 at 11:30 pm UTC is on the **November** statement |
| Uppercase labels, truncated to 30 characters | keep the labels as they come from the legacy, write its own in the same format |
| Operations sorted by date, most recent first | sort by date, not by arrival order (a legacy operation can arrive after a more recent transfer) |
| Uppercase errors: `{"error":"INSUFFICIENT BALANCE"}` | same statuses, same messages |
| Text statement, aligned columns, `\r\n` line endings, comma decimals ("1000,00") | the same text, character for character |

The comment: the legacy computes in cents then divides by 100 — 950 and not 950.00 in the JSON — and its representation
is reproduced.

```csharp
// The legacy system computes in cents then divides by 100: 950 and not 950.00 in the JSON. We reproduce its representation.
private static decimal Euros(decimal amount) => LegacyTranslator.Cents(amount) / 100m;
```

The **contract tests** ([EquivalenceTests.cs](../../22-StranglerFig/Fig.Tests/EquivalenceTests.cs)) build a varied
history (legacy fees, transfers on both sides, an operation at the turn of the month), then send the same requests to
both systems and demand responses **identical character for character**: 13 reads, 7 refused transfers. Three of the
gaps in the table above (amounts, time zone, sorting) were deliberately introduced into the new service to check that
these tests see them: they see them all.

The only accepted difference: a transfer's **reference** (`VIR-000001` for the legacy, `VIR-N00001` for the new one). It
is opaque to clients, and two distinct numberings avoid any collision when a route goes back and forth.

---

## 5. Rolling back

A problem in production on a migrated route? You give it back to the legacy, with the same request used to migrate it:

```http
PUT {{facade}}/facade/routes/transfers
X-Admin-Key: demo

{ "mode": "Legacy" }
```

It is the **return** synchronization that makes this safe: the legacy knows the transfers made by the new system. The
test `Rolling_back_a_route_loses_no_transfer` proves it: after a €100 transfer served by the new
system, then a return to the legacy, the latter refuses a €950 transfer (it knows only €900 is left). Without the
carry-over, it would have accepted it.

---

## 6. Switching the legacy off

All declared routes in `Modern` mode are not enough: there may still be calls nobody inventoried.
`GET /facade/progress` measures both:

```json
{ "declaredRoutes": 4, "migratedRoutes": 4, "percentage": 100,
  "undeclaredCalls": 0, "legacyCallsSinceLastSwitch": 0, "legacyRemovable": true }
```

`legacyRemovable` (legacy removable) requires every route migrated **and** no call to the legacy since the last
switchover. In the demo, `GET /branches` is the forgotten feature: a single call is enough to set the flag back to
`false`. If you switch the legacy off anyway, the four migrated routes keep working, and `/branches` answers `502`: it
had to be migrated too (test `Once_everything_is_migrated_the_legacy_system_can_be_switched_off`).

---

## The limits, shown rather than hidden

- **Reading on one side what was written on the other.** With the initial table, the lookup is served by the new system
  and transfers by the legacy. Right after a transfer, the lookup shows the **old balance**, until the next
  synchronization cycle (500 ms). Observed on the running demo, and pinned down in the test
  `Writing_on_one_side_and_reading_on_the_other_shows_a_late_balance_until_the_synchronization`. Countermeasure: switch
  the reads and writes of the same data **together**, or only open to the new system data it alone writes.
- **Two writers, one rule.** During the synchronization window, the legacy (its nightly fees) and the new system (its
  transfers) can debit the same account without seeing each other: an overdraft is possible. Every rule about a balance
  must be checked by **a single** system, or tolerate this lag.
- **The new system must be up to date before serving.** At startup, until the first cycle has run, it knows no account.
  In production: only send it traffic once the initial load is done (readiness probe).
- **The cost of coexistence**: two systems to operate, a synchronization to monitor, a translation layer to maintain,
  for as long as the migration lasts. A migration that drags on costs more than either system alone.

---

## Strangler Fig and the other demos

| | The façade's role | Lifetime |
|---|---|---|
| **Strangler Fig** | route each request to the legacy or the new system, for the duration of the migration | temporary: the table ends with everything in `Modern`, and the façade becomes a simple gateway |
| [API gateway](08-microservices.md) | single entry point, routing, security, quotas | permanent |
| [BFF](16-bff.md) | adapt the services to **one** client application | permanent |
| [Anti-corruption layer](11-ddd.md) | protect one model from another system's model | as long as the other system exists |

The pattern says nothing about the new system's shape: the new service can be organized according to any demo
(hexagonal, vertical slices…), and the target can be a [modular monolith](07-monolithe-modulaire.md) as well as
[microservices](08-microservices.md). You can also strangle **from the inside** ("branch by abstraction"): an interface
in front of part of the code, two implementations, a switch; the same principle without a network façade.

---

## The tests

The three processes run in memory, each with its real program; each link between them can be cut, slowed down, or lose
its responses, and an adjustable clock dates the operations on both sides.
([FigSystem.cs](../../22-StranglerFig/Fig.Tests/FigSystem.cs))

| Test | What is checked |
|---|---|
| `A_read_gives_the_same_response_on_both_sides` (13 cases), `A_refused_transfer_is_refused_with_the_same_error_on_both_sides` (7 cases) | **contract tests**: responses identical character for character |
| `The_inherited_contract_keeps_the_habits_of_the_legacy_system` | truncated label, Paris time without a time zone, `-2.5` and not `-2.50` |
| `A_route_nobody_took_over_goes_to_the_legacy_system`, `The_internal_routes_are_not_exposed_to_the_clients` | default routing, internal routes |
| `Switching_a_route_changes_who_answers_without_restarting_or_changing_anything_for_the_client` | live switch, `X-Served-By`, counters |
| `In_shadow_mode_the_client_receives_the_legacy_response_and_both_are_compared`, `A_divergence_is_reported_without_the_client_seeing_it` | shadow mode |
| `In_shadow_mode_a_slow_or_failed_modern_service_does_not_bother_the_client` | response in under 1.5 s when the new system takes 5; abandoned calls |
| `Shadow_mode_is_refused_for_a_write`, `Steering_the_migration_requires_the_admin_key` | control safeguards |
| `Rolling_back_a_route_loses_no_transfer` | safe rollback |
| `The_legacy_system_is_only_removable_when_nothing_calls_it_any_more`, `Once_everything_is_migrated_the_legacy_system_can_be_switched_off` | the end of the migration |
| `The_first_cycle_reloads_the_whole_legacy_system`, `What_an_internal_batch_of_the_legacy_system_writes_reaches_the_modern_one`, `A_transfer_made_in_the_modern_service_is_forwarded_to_the_legacy_system` + the three pitfalls in the table of section 3 | two-way synchronization |
| `A_log_row_becomes_an_operation_in_euros_with_its_time_zone`, `Paris_time_does_not_have_the_same_offset_in_winter`… | anti-corruption layer (no server) |
| `The_facade_and_the_modern_service_only_know_the_legacy_system_through_HTTP` | **architecture test** |

Checked by mutation (17 attempts, all detected): echo counted twice, non-idempotent import, forgotten carry-over, outbox
emptied before confirmation, undeclared route sent to the new system, internal routes forwarded, shadow awaited by the
client, shadow allowed for a write, a comparison that compares nothing, contract amounts, time zone or sorting
falsified, label not truncated, unknown log type ignored, balance rule forgotten by the new system, a removal flag that
no longer measures calls.

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| **Deliver right away**: each migrated route serves real traffic | Two systems to run together for as long as the migration lasts |
| **Reversible** at every step, with one request | Two-way synchronization: loops, failures, idempotency, lag |
| Shadow mode proves equivalence on real traffic, without risk | The legacy contract, quirks included, must be reproduced exactly |
| Clients change nothing: they only see the façade | Reading on one side what was written on the other: switch data in consistent blocks |
| You know when you're done: no more calls to the legacy | A migration that stops at 80% leaves the worst of both worlds |

**When to use it**: to replace a system **in production** whose service can't be stopped and whose evolution can't be
frozen for a rewrite, and whose features can be split into routes (or screens, messages). Less useful for a small
system rewritten in a few weeks, or when the legacy has no entry point that can be intercepted. In production: **YARP**
or the existing API gateway for the façade, **Debezium** (or database triggers) to capture changes, **Scientist**
(GitHub) or **Diffy** (Twitter) for shadow mode, and feature flags to switch by percentage of customers rather than all
at once.
