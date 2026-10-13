# 18. Actor Model

**French name**: *Modèle d'acteurs*

> **In one sentence**: the system is made of thousands of small objects, the *actors*, each with **its own state**,
> **sharing nothing** and communicating only **through messages**, handled **one at a time**; concurrency no longer
> needs any lock, and a failure is repaired by **replacing** the faulty actor.

📁 Code: [`18-ActorModel`](../../18-ActorModel) · ⬅️ [Cell-Based Architecture](17-cell-based.md) · ➡️ [Space-Based Architecture](19-space-based.md) ·
🌐 [Français](../18-modele-acteurs.md)

---

## The idea: no more shared state

In all the previous demos, the state is **shared**: several simultaneous requests read and modify the same accounts,
and it is the database (transactions, locks, concurrency control) that keeps them from stepping on each other. The actor
model (Carl Hewitt, 1973; popularized by Erlang) takes the problem the other way around: **each piece of data has a
single owner**, and you only talk to it by dropping a message into its mailbox.

```mermaid
flowchart LR
    subgraph Partage["SHARED STATE"]
        direction TB
        R1["Request 1"] --> V{{"lock"}}
        R2["Request 2"] --> V
        R3["Request 3"] --> V
        V --> S[("Alice's balance")]
    end
    subgraph Acteurs["ACTORS"]
        direction TB
        M1["Request 1"] -->|message| B["mailbox<br/>①②③"]
        M2["Request 2"] -->|message| B
        M3["Request 3"] -->|message| B
        B --> A["actor compte/alice<br/>balance: a plain field"]
    end
    Partage ~~~ Acteurs
```

On the left, safety depends on **every** piece of code that touches the balance: one forgotten lock, and updates are
lost. On the right, it is **structural**: only the actor touches its field, and it handles only one message at a time.
Its messages pile up in its mailbox, but **different** actors work in parallel, on all cores.

Three rules define an actor. In response to a message, it can:

1. **change its own state** (and only its own);
2. **send messages** to other actors whose address it knows;
3. **create** other actors (here they are *virtual*: you address them, the runtime creates them).

| | State sharing | A failure… |
|---|---|---|
| Demos 00 to 17 | shared data, protected by the database (transactions) | rolls back the transaction; the process continues |
| **Actors** | **nothing**: each state has a single owner | **replaces the faulty actor**, which reloads its durable state |

---

## The components

```mermaid
flowchart TB
    API["Actors.Api<br/>one route = one message"] -->|"Ask / Tell"| SYS["ActorSystem<br/>activation · dispatch · supervision · passivation"]
    SYS --> C1["compte/alice<br/>mailbox + state"]
    SYS --> C2["compte/bob<br/>mailbox + state"]
    SYS --> V1["virement/v-42<br/>mailbox + state"]
    V1 -->|"Debit"| C1
    V1 -->|"Credit"| C2
    C1 -.->|"writes its state"| ST[("StateStore<br/>one row per actor")]
    C2 -.-> ST
    V1 -.-> ST
```

- **The runtime** ([Actors.Runtime](../../18-ActorModel/Actors.Runtime), ≈ 300 lines written for the demo, no package):
  - [`Actor`](../../18-ActorModel/Actors.Runtime/Actor.cs): the base class, with `Start` (reload the state),
    `Receive` (handle a message) and `StopActivation` (stop);
  - [`ActorRef`](../../18-ActorModel/Actors.Runtime/ActorRef.cs): an actor's **address**
    (`account/1111…`), never the object itself; you `Tell` (send) it a message (*tell*, no reply) or `Ask` (ask)
    it (*ask*, with a reply and a timeout);
  - [`ActorSystem`](../../18-ActorModel/Actors.Runtime/ActorSystem.cs): one mailbox
    (`System.Threading.Channels`) and one loop per active actor, supervision, passivation.
- **The business** ([Actors.Bank](../../18-ActorModel/Actors.Bank)): [`AccountActor`](../../18-ActorModel/Actors.Bank/AccountActor.cs)
  (one per account), [`TransferActor`](../../18-ActorModel/Actors.Bank/TransferActor.cs) (one per transfer),
  immutable [messages](../../18-ActorModel/Actors.Bank/Messages.cs) (`record`), durable storage and a failure injector.
- **The API** ([Endpoints.cs](../../18-ActorModel/Actors.Api/Endpoints.cs)) neither reads nor writes any state:
  each route sends **one** message to **one** actor and translates its reply into an HTTP status code.

```csharp
app.MapPost("/accounts/{id:guid}/withdrawal", async (Guid id, AmountRequest r, ActorSystem system) =>
    Http(await system.Account(id).Ask(new Withdraw(r.Amount))));
```

In production, you don't write this runtime: **Orleans** (Microsoft, *virtual* actors, the model followed here),
**Akka.NET** (a port of Akka, Erlang-style supervision hierarchies) or **Proto.Actor** do the same thing, **spread over
several machines**: the address `account/alice` stays the same, whether the actor lives on server 1 or 7.

---

The code is quoted exactly as it is.

## 1. One message at a time: concurrency without locks

The whole model fits in this loop: a single reader per mailbox, which **waits for the end** of each message before
taking the next one — "THE key point of the model", as the comment says
([ActorSystem.cs](../../18-ActorModel/Actors.Runtime/ActorSystem.cs)).

```csharp
await foreach (var envelope in activation.Mailbox.Reader.ReadAllAsync())
{
    activation.Processing = true;
    object? response = null;
    Exception? failure = null;
    try
    {
        // THE key point of the model: only one message at a time, each handled to the end before the next one.
        response = await activation.Instance!.Receive(envelope.Message);
    }
    // …
}
```

The account can then write the most naive code there is: read the balance, check, modify. No `lock`, no transaction,
no optimistic version ([AccountActor.cs](../../18-ActorModel/Actors.Bank/AccountActor.cs)):

```csharp
if (debit && amount > state.Balance)
    return new Refusal($"Insufficient balance ({state.Balance:0.00} €).");

var signed = debit ? -amount : amount;
state.Balance += signed;
```

**Real run**: 1,000 €3 withdrawals fired **at the same time** on Alice's account (€1,000):

```json
POST /demo/simultaneous-withdrawals  { "account": "1111…", "count": 1000, "amount": 3 }
{ "balanceBefore": 1000, "requests": 1000, "accepted": 333, "refused": 667, "finalBalance": 1, "minimumObservedBalance": 1, "durationMs": 231 }
```

Exactly 333 withdrawals accepted (333 × 3 = €999), none twice, never a negative balance. The same code without an actor,
called by 1,000 threads, would lose updates.

A business refusal (insufficient balance) is a **reply** like any other (`Refusal`), not an exception: the actor carries
on. Exceptions are reserved for **failures** (see § 3).

---

## 2. Converse rather than lock: the transfer

A transfer touches two accounts, so two actors with separate states: there is **no shared transaction**. A third actor,
`transfer/{id}`, leads the conversation and records where it stands, like the [saga](14-saga-orchestree.md)'s
orchestrator, but at the scale of a single transfer.

```mermaid
sequenceDiagram
    participant API
    participant V as virement/v-42
    participant A as compte/alice
    participant B as compte/bob
    API->>V: ExecuteTransfer(alice, bob, 100)
    V->>V: state InProgress (stored)
    V->>A: Debit(key v-42, 100)
    A-->>V: AccountDto
    V->>V: state DebitDone (stored)
    V->>B: Credit(key v-42, 100)
    alt bob accepts
        B-->>V: AccountDto
        V->>V: state Completed
    else bob refuses (nonexistent account)
        B-->>V: Refused
        V->>A: Credit(key v-42-annulation, 100)
        V->>V: state Cancelled
    end
    V-->>API: TransferDto
```

Three properties follow from the model, with no dedicated code:

- **No deadlock.** The accounts never ask anyone for anything; only the transfer waits for their replies. With locks,
  simultaneous A→B and B→A deadlock unless you always lock in the same order. Real run: 500 transfers in each
  direction, at the same time.

  ```json
  POST /demo/crossed-transfers  { "count": 500, "amount": 1 }
  { "transfers": 1000, "completed": 572, "refused": 428, "totalBefore": 501, "totalAfter": 501, "durationMs": 518 }
  ```

  (Alice only had €1 left after the previous run, hence the refusals; the total, however, is preserved.)
- **Identity provides idempotency.** Replaying `transfer/invoice-42` wakes up the **same** actor, which already knows the
  answer; a finished transfer (done, refused or cancelled) never starts again — the comment: already finished, same
  reply, no effect:

  ```csharp
  if (_state is { Status: TransferStatus.Completed or TransferStatus.Refused or TransferStatus.Cancelled })
      return View();  // already finished: same response, no effect
  ```

- **Recovery without a double debit.** If the transfer crashes after the debit, its stored state says `DebitDone`:
  replayed, it skips the debit and resumes at the credit. And even if redone, the debit carries the key
  `transfer-v-42`, which the account recognizes:

  ```csharp
  if (key is not null && state.Operations.Any(o => o.Key == key))
      return View();
  ```

⚠ The model's trap: an actor that **waits** for a reply handles nothing else meanwhile. If account A asked account B
for something while B asked A for something, they would wait for each other (until the request's timeout). Hence the
rule followed here: **requests go in one direction only** (transfer → accounts), never in a cycle; when an actor doesn't
need the reply, it `Tell`s without waiting.

---

## 3. "Let it crash": supervision

Rather than anticipating every error in the business code, you let the actor crash and its **supervisor** **replaces**
it: the faulty incarnation, with a possibly inconsistent in-memory state, is thrown away; a fresh one reloads the last
**written** state and picks up the following messages, which were waiting in the mailbox.

```mermaid
flowchart LR
    M["Next message<br/>from the mailbox"] --> T{"Receive"}
    T -->|"reply or Refused"| OK(["Next message"])
    T -->|"exception"| S{"Failures in<br/>the last minute?"}
    S -->|"3 at most"| R["Throw away the incarnation,<br/>create a fresh one<br/>that rereads the storage"]
    R --> OK
    S -->|"the 4th"| Q(["Stop: quarantine,<br/>messages refused (503)<br/>until the end of the window"])
```

```csharp
if (activation.Failures.Count > strategy.MaxRestarts)
{
    await StopActivation(activation, $"{activation.Failures.Count} failures in {strategy.Window.TotalSeconds:0} s ({failure.Message})");
    return false;
}

await Deincarnate(activation);
if (!await Incarnate(activation))
```

The most telling case: a failure **at the worst moment**, after the balance was changed in memory but before it was
written (`Step.BeforeSave`, armed with `POST /system/failures`). Real run (the API's messages):

```text
POST /accounts/1111…/withdrawal { "amount": 1 }  → 500 "account/1111… crashed while handling this message: simulated failure (BeforeSave)"
GET  /accounts/1111…                            → 200, balance unchanged: the withdrawal did not happen
GET  /accounts/2222… (trapped 4 times)          → 500 500 500 500 503
```

No half-modified balance, no cleanup code: the new incarnation starts again from the durable state. Beyond 3 restarts
per minute, restarting is pointless: the actor is **stopped** and its messages refused (`503`) until the end of the
window. The other actors notice nothing (Alice answers while Bob is in quarantine).

A failure **propagates along requests**: if Bob's account crashes during a transfer, the transfer's request fails, and
the transfer crashes in turn. Both are restarted, and replaying the transfer finishes it (test
`An_account_failure_propagates_to_the_transfer_which_can_be_replayed`).

The demo's runtime applies **one strategy per actor type** (Orleans' model, where the runtime supervises everyone).
Akka and Erlang go further: actors form a **tree**, each parent supervises its children and can **escalate** the
failure to its own parent.

Finally, a slow actor only blocks its own callers: `Ask` has a timeout (5 s), beyond which the API answers `504`.
The message itself stays in the mailbox and will be handled.

---

## 4. Virtual actors: activation and passivation

In Orleans, an actor (a *grain*) **always exists** in principle: you neither create nor destroy it. The first message
**activates** it (an in-memory *incarnation*), inactivity **passivates** it (its memory is freed, its stored state
stays), the next message reactivates it. You can therefore have millions of accounts and keep in memory only those in use.

```text
POST /system/passivation           → { "passivated": 1004 }      Alice, 9999… (touched by the compensation), 1,002 transfers
GET  /system                       → { "active": 0, ... }
GET  /system/storage?type=account  → ["account/1111…", "account/2222…"]   9999… was never opened: nothing stored
GET  /accounts/1111…               → 200, balance restored
GET  /system                       → { "active": 1, ... }      only Alice was woken up
```

(1,004 actors put to sleep: Alice, the nonexistent account 9999… touched by the compensation, and 1,002 transfers;
9999… was never opened, so nothing is stored for it; only Alice is woken up again.)

Automatic passivation runs every 5 s and puts to sleep the actors idle for 30 s
([AutomaticPassivation.cs](../../18-ActorModel/Actors.Api/AutomaticPassivation.cs)). An actor with pending
or in-progress messages is never passivated.

The central guarantee of a virtual actor runtime: **at most one incarnation per actor at a time**. The tricky case is a
message arriving while a passivation is stopping the old incarnation: the new one waits for it before starting — never
two incarnations of the same actor at once, even for a split second.

```csharp
// If a previous incarnation is still finishing stopping (passivation), the new one waits for it before starting:
// never two incarnations of the same actor at the same time, not even for a fraction of a second.
_finishingIncarnations.Remove(path, out var previous);
activation.Loop = Task.Run(() => Run(activation, previous));
```

(Across several machines, guaranteeing this uniqueness is Orleans' real work: a distributed directory says which
server hosts which actor.)

---

## 5. The price: what actors make hard

- **Cross-cutting reads.** "List all accounts", "total deposits": you would have to wake up and query every actor. The
  API has no `GET /accounts`, for that matter. You build a **read view** alongside, fed by the actors' events: that is
  [CQRS](09-cqrs.md), often paired with actors.
- **No transactions between actors**: a process involving several actors is written step by step, with stored states,
  idempotency and compensation (§ 2).
- **Request cycles** deadlock (§ 2); mailboxes can **overflow** if an actor receives more than it handles (a heavily
  solicited account is a bottleneck: one message at a time is also a throughput limit).
- **Delivery**: an `Tell` to a quarantined actor is lost ("dead letter", counted in `/system`); between machines, a
  message can be lost or duplicated. Hence the idempotency keys.
- **Debugging**: a request's flow is scattered across asynchronous messages between actors; you need a log (here
  `/system/log`) and, in production, distributed tracing.

---

## The tests

Two levels: the runtime alone, with an instrumented toy actor
([RuntimeTests.cs](../../18-ActorModel/Actors.Tests/RuntimeTests.cs)), then the business
([BankTests.cs](../../18-ActorModel/Actors.Tests/BankTests.cs)) and the API
([ApiTests.cs](../../18-ActorModel/Actors.Tests/ApiTests.cs)). A manual clock moves time forward for passivation
and quarantine. The toy actor increments its counter in the most dangerous way possible:

```csharp
case Increment:
    var read = _counter;
    await Task.Yield();
    _counter = read + 1;
    return _counter;
```

| Test | What is checked |
|---|---|
| `An_actor_handles_only_one_message_at_a_time` | 1,000 simultaneous increments: none lost, never two messages in progress |
| `Different_actors_work_in_parallel` | two actors, two messages at the same time |
| `The_messages_of_a_sender_are_handled_in_sending_order` | mailbox order |
| `The_first_message_activates_the_actor_and_inactivity_passivates_it`, `A_busy_actor_is_never_passivated` | virtual actors |
| `Never_two_incarnations_of_the_same_actor_at_the_same_time` | messages and passivations colliding, slow stops |
| `A_failure_replaces_the_incarnation_without_losing_the_next_messages`, `A_failure_does_not_affect_the_other_actors` | supervision |
| `Too_many_failures_stop_the_actor_until_the_end_of_the_window` | quarantine, dead letters |
| `A_request_that_takes_too_long_expires_without_the_message_being_lost` | a request's timeout |
| `A_thousand_simultaneous_withdrawals_never_make_the_balance_negative` | 333 accepted out of 1,000, balance €1 |
| `Simultaneous_crossed_transfers_do_not_deadlock` | 200 + 200 transfers, total preserved |
| `A_failure_before_saving_cancels_the_withdrawal_in_progress` | the state reread from storage |
| `A_transfer_interrupted_after_the_debit_completes_when_replayed`, `La_panne_d_un_compte_remonte_au_virement…` | recovery without a double debit |
| `A_transfer_to_an_account_that_does_not_exist_is_compensated`, `Replaying_a_refused_transfer_never_credits_the_target` | compensation, final refusal |
| `Replaying_a_completed_transfer_returns_the_same_result_without_effect`, `A_debit_replayed_with_the_same_key_is_applied_only_once` | idempotency: actor identity, operation key |
| `Business_refusals_are_responses_not_failures` | a refusal restarts nothing |
| `A_passivated_account_finds_its_balance_again_in_the_storage` | passivation, waking up a single actor |
| `Repeated_failures_put_the_actor_in_quarantine`, `A_slowness_answers_504_without_blocking_the_other_accounts` | `500`, `503`, `504` over HTTP |

Checked by mutation: creating one incarnation per message, not waiting for the previous incarnation, keeping the faulty
incarnation after a failure, passivating a busy actor, removing the quarantine, the idempotency of debits, the
compensation or the "transfer already finished" check each make at least one test fail.

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| **Concurrency without locks**: each state has a single owner | No transactions between actors: steps, idempotency, compensation |
| Simple, sequential business code inside each actor | Cross-cutting reads are hard (a separate view is needed) |
| **Local failures**: the actor is replaced, the durable state is authoritative | Request cycles = deadlocks; a heavily solicited actor = bottleneck |
| Scales: millions of actors, spread over machines | Harder debugging and tracing (everything is asynchronous) |

**When to use it**: many independent entities, each with a state that changes often and concurrent access: players
and games in an online game (Halo uses Orleans), connected objects, sessions, carts, accounts, messaging (WhatsApp on
Erlang), telecom systems. Less suited when most of the work is cross-cutting queries over relational data. In .NET:
**Orleans** for virtual actors without lifecycle management, **Akka.NET** or **Proto.Actor** for fine-grained control
(supervision hierarchies, streams).
