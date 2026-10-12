# 19. Space-Based Architecture

**French name**: *Architecture spatiale*

> **In one sentence**: the database is taken off the request path; each **processing unit** keeps **all the data in
> memory**, in a grid replicated between the units, and the database is only updated in the background. Units can then
> be added and removed at will to absorb the load.

📁 Code: [`19-SpaceBased`](../../19-SpaceBased) · ⬅️ [Actor Model](18-modele-acteurs.md) · ➡️ [Pipeline](20-pipeline.md) ·
🌐 [Français](../19-space-based.md)

---

## The idea: the database is the bottleneck

All the previous demos end up the same way: each request reads from or writes to a database. You can multiply the
application servers, the database stays **single** and ends up limiting throughput (locks, connections, disk). The name
comes from the idea of a *tuple space*: a shared data space in memory. The space-based architecture (described notably
by Mark Richards) removes the database from the request path.

```mermaid
flowchart LR
    subgraph Classique["CLASSIC"]
        direction TB
        C1["Server 1"] --> DB[("Database")]
        C2["Server 2"] --> DB
        C3["Server 3"] --> DB
    end
    subgraph Spatiale["SPACE-BASED"]
        direction TB
        U1["Unit 1<br/>+ in-memory grid"] <-->|"replication"| U2["Unit 2<br/>+ in-memory grid"]
        U1 -.->|"pump, later"| DB2[("Database")]
        U2 -.-> DB2
    end
    Classique ~~~ Spatiale
```

A request only waits for memory now: reading an account is a dictionary lookup, writing it too. The database becomes a
**backup** updated later, and is only used to reload the grid when all the units have gone down.

| | The reference data is… | To handle more load… |
|---|---|---|
| Demos 00 to 18 | in the database | you add servers, the database stays single |
| [Cells](17-cell-based.md) | in each cell's database | you add cells, each with its database |
| **Space-based** | **in memory**, in each unit | you add units; the database follows at its own pace |

---

## The components

```mermaid
flowchart LR
    CL["Clients"] --> GM["<b>Messaging grid</b><br/>affinity or round robin"]
    GD["<b>Deployment<br/>manager</b>"] -.->|"adds, removes"| GM
    GM --> U1["Unit u1<br/>business + replica"]
    GM --> U2["Unit u2<br/>business + replica"]
    U1 <-->|"replication"| U2
    U1 --> P[["Data pump"]]
    U2 --> P
    P -->|"writer,<br/>in the background"| DB[("Database")]
```

On a cold start (no unit left), the **data reader** reloads the grid from the database. What is called the
**virtualized middleware** ([Space.Grid](../../19-SpaceBased/Space.Grid), simulated in-process for the demo, no package):

- the **data grid** ([DataGrid.cs](../../19-SpaceBased/Space.Grid/DataGrid.cs),
  [Replica.cs](../../19-SpaceBased/Space.Grid/Replica.cs)): a full replica per unit, and asynchronous replication of
  each write to the others (100 ms of latency, adjustable);
- the **messaging grid** ([MessagingGrid.cs](../../19-SpaceBased/Space.Grid/MessagingGrid.cs)): the entry
  point, which picks the unit for each request;
- the **data pump** and its **writer**, the **data reader** ([Database.cs](../../19-SpaceBased/Space.Grid/Database.cs));
- the **deployment manager** ([DeploymentManager.cs](../../19-SpaceBased/Space.Grid/DeploymentManager.cs)).

The **processing units** ([ProcessingUnit.cs](../../19-SpaceBased/Space.Grid/ProcessingUnit.cs)) contain the
business ([Bank.cs](../../19-SpaceBased/Space.Bank/Bank.cs)), which only sees its own unit's replica (the code
is quoted exactly as it is, French messages included):

```csharp
private static object Movement(UnitContext unit, Guid id, decimal amount, bool debit, string label) =>
    unit.Modify<Account>(id.ToString(), account =>
        account is null ? (null, NotFound(id))
        : amount <= 0 ? (null, new Refusal("The amount must be positive."))
        : debit && amount > account.Balance ? (null, new Refusal($"Insufficient balance ({account.Balance:0.00} €)."))
```

In production: **Hazelcast**, **Apache Ignite**, **GigaSpaces** (which popularized the model), **Oracle Coherence** or
**Infinispan** provide the grid and its replication, across several machines; the pump is a durable queue (Kafka,
RabbitMQ). This model is found in ticketing, online auctions, trading.

---

## 1. Reading and writing without the database

Each write is applied in the unit's replica, then **published**: to the other replicas, and to the pump. The request
waits for neither — the database is not on the request path.

```csharp
private void Publish(string origin, Modification modification)
{
    Interlocked.Increment(ref _inFlight);
    _queue.Writer.TryWrite((origin, modification, DateTimeOffset.UtcNow + ReplicationLatency));
    // The database is not on the request path: the write goes into the pump, we do not wait.
    _toThePump(modification);
}
```

The consequence is spectacular: **the database can go down, requests carry on**. Writes pile up in the pump and are
written when the database comes back (test `Requests_never_wait_for_the_database`). Meanwhile, the database lags
behind: a report reading it would see stale data.

`GET /grid/comparison/{id}`, right after a withdrawal, shows the **replication window** (real run; u9 hasn't received
the write yet, 300 ms later it is everywhere):

```text
{ "replicas": [ { "where": "u8", "version": "v2@u8", "balance": 495 },
                { "where": "u9", "version": "v1@database", "balance": 500 } ],     ← u9 hasn't received the write yet
  "database": { "version": "v2@u8", "balance": 495 } }
300 ms later: v2@u8 and 495 everywhere.
```

Each entry carries a **version** (a counter and the unit that wrote it). Replicas and database apply the same rule: the
highest version wins. They therefore all end up identical, whatever the order in which writes arrive.

---

## 2. The price: collisions

Two units can modify **the same data at the same time**, each in its own replica, before receiving the other's write.
On replication, the highest version wins and **the other write is lost**, even though its client received "OK".

```mermaid
sequenceDiagram
    participant C1 as Client 1
    participant U1 as u1 (Alice: 1,000)
    participant U2 as u2 (Alice: 1,000)
    participant C2 as Client 2
    C1->>U1: withdraw 50
    C2->>U2: withdraw 100
    U1-->>C1: OK, 950 (v2@u1)
    U2-->>C2: OK, 900 (v2@u2)
    U1->>U2: replication v2@u1: lower, ignored
    U2->>U1: replication v2@u2: higher, overwrites
    Note over U1,U2: Alice: 900 everywhere. €150 withdrawn, €100 debited.
```

The replica detects the collision when a modification doesn't start from the version it holds:

```csharp
var concurrent = local != m.Previous && local.Unit != "" && local.Unit != m.Version.Unit;
if (m.Version > local)
{
    _entries[m.Key] = new Entry(m.Key, m.Value, m.Version);
    return new MergeResult(true, concurrent ? local : null);
}
```

The countermeasure lies in the **messaging grid**: **affinity routing** always sends the same key to the same unit,
which is then the only one writing it. "Rendezvous" hashing limits movements when the number of units changes:

```csharp
: _units.MaxBy(u => Hash($"{key}/{u.Id}"))!;
```

**Real run**: 200 €1 withdrawals fired at the same time on Alice's account, depending on the routing.

```json
POST /demo/concurrent-withdrawals  (affinity)
{ "unitsUsed": ["u2"], "accepted": 200, "expectedBalance": 800, "balanceInGrid": [800], "balanceInDatabase": 800, "lostUpdates": 0 }

POST /demo/concurrent-withdrawals  (round robin)
{ "unitsUsed": ["u5","u6"], "accepted": 200, "expectedBalance": 600, "balanceInGrid": [700], "balanceInDatabase": 700, "lostUpdates": 199 }
```

With round robin (*tourniquet*), 200 withdrawals accepted but only €100 debited: the bank lost €100. Grid and database
are nevertheless consistent with each other: nothing flags the error, except the collision counter.

⚠ **Affinity isn't always enough.** When a unit arrives or leaves, some keys change owner; during the replication
window, the old and the new unit can write the same key. The real run showed it: right after a load peak, while the
manager was removing units, one of Bob's withdrawals was lost (test
`With_affinity_a_change_of_units_opens_a_collision_window`). Real products reduce this risk by migrating
partitions one by one, or by keeping a single master copy per item (a **distributed** rather than **replicated** cache,
at the cost of a network call when the data is elsewhere).

Mark Richards gives an estimate of the collision rate: it grows with the **number of units**, the **square** of the
update rate, and the **replication latency**, and shrinks with the cache size. This model therefore suits data that is
**read often, written rarely**, or well partitioned.

---

## 3. Elasticity

The deployment manager assesses the load every 250 ms (requests in progress or waiting, relative to a capacity of 4 per
unit): above 75%, it adds a unit; below 25%, it removes one (2 minimum, 6 maximum).

```csharp
if (load >= options.HighThreshold && units.Count < options.MaxUnits)
{
    var unit = await Start();
    return $"load {load * 100:0}%: {unit.Id} added";
}
```

A new unit receives a **copy of the grid from an existing unit**, without touching the database. **Real run**: 600
simultaneous account openings.

```json
POST /demo/load  { "count": 600 }
{ "durationMs": 2373, "unitsBefore": 2, "maxUnits": 6, ... }
```

```text
08:23:43.612 u10 started: grid copied from u8 (46 entries)
08:23:43.858 u11 started: grid copied from u8 (106 entries)
...
08:23:46.099 u9 stopped (292 requests processed)
```

Starting a unit only costs a memory copy: that is what makes elasticity fast. Queued requests stay on their unit; only
new ones benefit from the added units.

---

## 4. Failures: one unit, all the units

- **One unit goes down**: its data is also in the other replicas. The messaging grid takes its requests away from it,
  including those waiting in its queue, and hands them to another (test
  `The_requests_of_a_failed_unit_are_handed_to_another_one`). Hence the minimum of **2 units**: a single failure must
  never empty the grid.
- **All the units go down**: the in-memory grid is gone. The next request triggers a **cold start**: the data reader
  reloads the grid from the database. But only **after the pump has written everything** — otherwise stale values would
  be loaded, and the late writes would then overwrite the new unit's ones in the database:

  ```csharp
  // DATA READER. First wait for the pump to have written everything: otherwise we would load stale values,
  // and the late writes would then overwrite in the database those of the new unit.
  ```

  If the database is down too, there is no way to restart: `503`. Real run: database down, 3 withdrawals accepted
  (`200`), total failure → `503`; database restored → the next request waits 6.8 s for the pump to write its 444
  pending writes, then answers with the right balance.

---

## The tests

Platform tests with the real business ([PlatformTests.cs](../../19-SpaceBased/Space.Tests/PlatformTests.cs)),
where replication can be **held back by hand** to make a collision certain (each unit writes before seeing the
other's write), and API tests ([ApiTests.cs](../../19-SpaceBased/Space.Tests/ApiTests.cs)).

```csharp
_p.Messages.Mode = RoutingMode.RoundRobin;
_p.Data.Suspend();  // each unit writes before having seen the other's write

var first = await Send(new Withdraw(Alice, 100m));  // u2
var second = await Send(new Withdraw(Alice, 50m));    // u1
```

| Test | What is checked |
|---|---|
| `A_write_is_replicated_in_all_the_units_then_in_the_database` | replication, pump |
| `Requests_never_wait_for_the_database` | database down: requests go through, the pump catches up |
| `With_affinity_a_given_key_always_goes_to_the_same_unit`, `With_affinity_simultaneous_withdrawals_lose_nothing` | affinity |
| `With_round_robin_two_units_writing_the_same_data_collide` | collision, lost write, database identical to the grid |
| `With_affinity_a_change_of_units_opens_a_collision_window` | the limit of affinity |
| `Load_adds_units_and_calm_removes_them` | elasticity, ceiling, floor |
| `A_new_unit_receives_a_copy_of_the_grid_without_reading_the_database` | copy between units |
| `The_failure_of_a_unit_loses_no_data`, `The_requests_of_a_failed_unit_are_handed_to_another_one` | failure of one unit |
| `After_a_total_failure_the_cold_start_reloads_the_grid_from_the_database`, `The_cold_start_waits_for_the_pump_to_have_written_everything` | cold start |
| `A_transfer_debits_in_the_source_unit_and_credits_in_the_target_unit`, `A_credit_refused_by_the_target_unit_is_compensated` | transfer between units, compensation |
| `Crossed_transfers_between_two_full_units_do_not_block` | internal requests take no queue slot |
| `Database_down_requests_go_through_but_a_cold_start_is_impossible` | `200`, then `503`, then recovery, over HTTP |
| `Each_response_tells_which_unit_handled_it`, `Units_can_be_added_stopped_and_brought_down`, `The_comparison_shows_the_same_version_everywhere_at_rest` | the API: `X-Unit` header, unit management, convergence |

Checked by mutation: always round-robin routing, no collision detection, a database or replicas ignoring versions, a
cold start that doesn't wait for the pump, a new unit loaded from the database, a pump without retries, queued internal
requests, no rerouting after a unit failure, no compensation, no unit added under load: each makes at least one test fail.

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| **The database is no longer the bottleneck**: reads and writes in memory | **Collisions**: accepted writes can be lost |
| Fast **elasticity**: a unit starts with a memory copy | All the data must fit in memory, in every unit |
| The database can go down without interrupting the service | The database lags: reports and other systems see stale data |
| Load peaks absorbed (ticketing, auctions, sales) | Complex to test and operate; grid products are expensive |

**When to use it**: very variable, very high load with unpredictable peaks (ticketing when sales open, auctions,
bookings, games), when the database can't keep up, and for data that fits in memory and is mostly written by key. Avoid
it for large data volumes, frequent concurrent writes to the same data, or when the database must be up to date at all
times (accounting, real-time reports). For a real bank, it would be a poor choice for balances; the demo uses them
precisely because the collisions show.
