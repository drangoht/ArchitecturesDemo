# 20. Pipeline (pipes & filters)

> **In one sentence**: a processing job is split into independent **filters**, each doing a single thing, connected by
> **pipes**; data flows from one to the next, each filter working **at the same time** as the others on a different
> item, like an assembly line.

📁 Code: [`20-Pipeline`](../../20-Pipeline) · ⬅️ [Space-Based Architecture](19-space-based.md) · ➡️ [Choreography](21-choregraphie.md) ·
🌐 [Français](../20-pipeline.md)

---

## The idea: an assembly line

It is the oldest of the architectures in this repository: that of the Unix shell
(`cat transfers.csv | grep EUR | sort | uniq`), of compilers (lexical → syntactic → semantic analysis → generation), of
ETL and stream processing. It doesn't apply to a whole application, but to a **data processing job**: here, importing a
file of transfers sent by a company (thousands of lines to check, then execute).

```mermaid
flowchart LR
    F[/"CSV file"/] --> A["Parse"] --> V["Validate"] --> D["Deduplicate"] --> C["Convert"] --> FR["Check<br/>for fraud ×N"] --> E["Execute"] --> S(["Executed<br/>transfers"])
    A -.-> R[("Rejects<br/>by reason")]
    V -.-> R
    D -.-> R
    C -.-> R
    FR -.-> R
    E -.-> R
```

Three rules make the architecture:

- a **filter** knows neither its source, nor its destination, nor the other filters: it receives an item, produces
  another one, or **rejects** it;
- a **pipe** connects two filters, and it is **bounded**: when it is full, the previous filter waits;
- all the filters work **at the same time**: while *Execute* handles line 1, *Check for fraud* handles line 2, *Convert*
  line 3…

| | A processing job is… | To speed it up… |
|---|---|---|
| A classic service | a method that does everything, line after line | optimize the method |
| **Pipeline** | a sequence of filters working in parallel | find the **bottleneck** and parallelize **that** stage |

---

## The components

- **The engine** ([Pipeline.Core](../../20-Pipeline/Pipeline.Core), generic, no package, on `System.Threading.Channels`):
  [`IFilter`](../../20-Pipeline/Pipeline.Core/Filter.cs), the stages and their bounded pipes
  ([Stage.cs](../../20-Pipeline/Pipeline.Core/Stage.cs)), assembly and execution ([Pipeline.cs](../../20-Pipeline/Pipeline.Core/Pipeline.cs)).
- **The banking filters** ([Filters.cs](../../20-Pipeline/Pipeline.Bank/Filters.cs)) and their assembly
  ([Imports.cs](../../20-Pipeline/Pipeline.Bank/Imports.cs)).
- **The API** ([Endpoints.cs](../../20-Pipeline/Pipeline.Api/Endpoints.cs)): the body of `POST /imports` *is* the
  file, read line by line as it arrives.

A filter boils down to one method — a rejection is a normal result (the line is set aside, the stream continues); an
exception stops the whole pipeline (the code is quoted exactly as it is, French comments included):

```csharp
public interface IFilter<in TInput, TOutput>
{
    string Name { get; }

    /// <summary>A rejection is a normal result (the line is discarded, the flow goes on); an exception stops the whole pipeline.</summary>
    ValueTask<Outcome<TOutput>> Process(TInput input, CancellationToken cancellation);
}
```

The assembly reads like the diagram, and the compiler checks that each filter's output is the next one's input:

```csharp
Core.Pipeline.From<string>()
    .Then(new Parse())
    .Then(new Validate())
    .Then(new Deduplicate(ledger))
    .Then(new ConvertCurrency())
    .Then(new CheckFraud(options.ScoringCost), options.FraudParallelism, options.Ordered);
```

In production: **TPL Dataflow** or **Channels** directly within a process, **Akka Streams**; at system scale, filters
become services and pipes become queues or streams (**Kafka Streams**, **Apache Beam**, **Azure Data Factory**, ETL and
CI pipelines).

---

## 1. Rejects: setting a line aside without stopping the file

A faulty line doesn't interrupt the import: the filter that detects it **rejects** it with a reason, and the stream
continues. The report counts the rejects by reason and keeps the first ones, with their line and the filter that set
them aside. **Real run** with [Transfers-sample.csv](../../20-Pipeline/Pipeline.Api/Transfers-sample.csv) (11 lines;
the report is in French: malformed line, invalid IBAN, identical accounts, duplicate, unknown currency, blacklisted
beneficiary, limit exceeded, insufficient balance):

```text
line 3   Parse              malformed line                unreadable amount "twelve"
line 4   Validate           invalid IBAN                  FR7630006000011234567890188
line 5   Validate           identical accounts
line 6   Deduplicate        duplicate                     in the file
line 7   Convert currency   unknown currency              JPY
line 8   Check fraud        payee on blocklist
line 9   Check fraud        ceiling exceeded              10530,00 €
line 10  Execute            insufficient balance          VIR-009
→ 3 transfers executed: Alice 842.80 €, Bob 657.20 €
```

An **exception**, on the other hand, signals a failure: the whole pipeline stops (the first failing stage cancels the
others).

```csharp
// The first stage that fails stops all the others.
foreach (var task in tasks)
    _ = task.ContinueWith(_ => stopping.Cancel(), TaskContinuationOptions.OnlyOnFaulted);
```

---

## 2. Recomposing: the same filters, another pipeline

Since the filters don't know each other, they can be reassembled at will. The **dry-run import**
(`POST /imports?dryRun=true`) is the same pipeline **without the last filter**: it tells what would be rejected, without
touching a single account.

```csharp
public static Pipeline<string, Transfer> DryRun(Ledger ledger, ImportOptions options) => Checks(ledger, options).Build();
```

Likewise, the demo's simulated failure is a filter inserted before *Execute*, and the tests assemble their own pipelines
with test filters. Two assembly tips:

- **cheap filters first**: whatever *Parse* or *Deduplicate* rejects never reaches the expensive fraud check;
- the filters are independent, but the pipeline has a **logical order**: *Execute* assumes *Convert* has run (amount in
  euros). It therefore checks its precondition rather than trusting the assembly (test
  `Execute_refuses_a_transfer_that_did_not_go_through_the_conversion`, written after this trap showed up).

---

## 3. Back-pressure: a file of any size, bounded memory

Pipes hold at most 64 items. When a stage is slower than the previous one, its input pipe fills up, and the previous
stage **waits** to write; the wait propagates all the way back to the file reading. Writing to a full pipe BLOCKS: that
is back-pressure.

```csharp
/// <summary>Writing into a full pipe BLOCKS: the stage waits for the next one to have room. That is the back-pressure.</summary>
private async Task Write(ChannelWriter<Element<TOutput>> output, Element<TOutput> element, RunContext context)
{
    var start = Stopwatch.GetTimestamp();
    await output.WriteAsync(element, context.Cancellation);
```

Result: the file is never loaded in full. **Real run**, 100,000 lines generated on the fly: at most **330 to 375 lines
in memory** at a time depending on parallelism (`maxEnTransit`), and it would be the same for 10 million.

---

## 4. The bottleneck, and a stage's parallelism

A pipeline's throughput is that of its **slowest stage**. The report measures, for each stage, its processing time and
its time waiting on the next pipe. **Real run**, 100,000 lines (1 copy of the fraud check: 4,117 ms, 24,000 lines/s,
fraud check 94% busy; 8 copies: 763 ms, 131,000 lines/s, same result):

```text
1 copy of the fraud check: 4,117 ms (24,000 lines/s)
   Parse         busy 0.02   waiting for the next 3,976 ms
   Validate      busy 0.05   waiting for the next 3,847 ms
   Check fraud   busy 0.94   ← the bottleneck
   Execute       busy 0.01
8 copies: 763 ms (131,000 lines/s), same result
```

The fast stages spend their time waiting: only the bottleneck matters. A **stateless** filter (like the fraud check) can
run in **several copies**; a stateful filter (*Deduplicate*, *Execute*) stays single. And once the bottleneck is
widened, another takes its place: with a light fraud score, the bottleneck becomes *Validate* (IBAN computation), and
adding fraud copies no longer helps.

The trap: several copies finish out of order. Yet **file order matters**: if Alice transfers €600 to Bob on line 1, and
Bob €1,000 on line 2, line 2 is only possible after line 1. The parallel stage therefore numbers the items and releases
them in arrival order:

```csharp
while (early.Remove(expected, out var next))
{
    if (next is not null)
        await Write(output, next, context);
    expected++;
    window.Release();
}
```

Without this, line 2 reaches *Execute* first and is rejected (test
`The_file_order_matters_and_the_ordered_parallel_stage_respects_it`). Parallelizing therefore doesn't change the
result (test `Parallelizing_the_fraud_check_does_not_change_the_result`).

---

## 5. Recovery after a failure

An import can stop halfway (failure, deployment, database unavailable). Some lines are executed, others not. The cure
is **idempotency**: *Deduplicate* drops any already executed reference, and the ledger refuses, as a last resort, to
execute the same one twice. You **simply replay the file**. Real run (VIR-001 had been executed despite the failure;
the replay executes 2, with 2 duplicates, and ends with the same balances as without a failure):

```text
POST /imports?failureAtReference=VIR-010   → 500, "Simulated failure while processing VIR-010.", summary: 0 executed
GET  /accounts/<Alice>                     → 880 €: yet VIR-001 had been executed
POST /imports                              → 2 executed, duplicates: 2; Alice 842.80 €, Bob 657.20 € (as without failure)
```

⚠ The report of a **failed** import tells what *came out* of the pipeline, not what was *done*: VIR-001 was executed,
but its output was still waiting in the last pipe when the stop cut everything. Only the ledger is authoritative: that
is why recovery relies on the executed references, not on the report.

---

## The tests

The engine with test filters ([CoreTests.cs](../../20-Pipeline/Pipeline.Tests/CoreTests.cs)), the banking filters
assembled as in the API ([ImportTests.cs](../../20-Pipeline/Pipeline.Tests/ImportTests.cs)), and the API
([ApiTests.cs](../../20-Pipeline/Pipeline.Tests/ApiTests.cs)).

| Test | What is checked |
|---|---|
| `The_items_go_through_the_filters_in_order`, `A_rejection_discards_the_item_without_stopping_the_flow` | the stream, rejects by reason |
| `The_stages_work_at_the_same_time_each_on_one_item` | 3 stages busy at the same time |
| `Back_pressure_bounds_what_is_in_memory` | 20,000 items, at most 37 in transit |
| `An_ordered_parallel_stage_returns_the_items_in_their_arrival_order` | ordered or not, with a very slow item |
| `An_exception_in_a_filter_stops_the_whole_pipeline`, `Cancellation_stops_an_endless_flow` | failure, cancellation |
| `The_bottleneck_is_the_busiest_stage_and_each_run_has_its_counters` | metrics |
| `The_sample_file_gives_three_transfers_and_one_rejection_per_reason` | each reject by the right filter, balances |
| `Replaying_a_file_executes_nothing_twice`, `The_ledger_never_executes_the_same_reference_twice` | idempotency, at two levels |
| `After_a_failure_in_the_middle_of_an_import_the_replay_finishes_without_executing_anything_twice` | recovery |
| `A_dry_run_import_tells_what_would_go_through_without_modifying_anything` | recomposition |
| `The_file_order_matters_and_the_ordered_parallel_stage_respects_it`, `Parallelizing_the_fraud_check_does_not_change_the_result` | order and determinism |
| `Execute_refuses_a_transfer_that_did_not_go_through_the_conversion` | a filter's precondition |
| `A_big_file_flows_through_with_bounded_memory` | 20,000 lines, expected rejects, money preserved |
| `The_check_digits_of_an_iban_are_verified` | mod 97 |
| `An_import_executes_the_file_and_keeps_its_report`, `A_dry_run_import_touches_no_account`, `A_failure_answers_500_with_the_report`, `The_bulk_import_demo_points_out_the_bottleneck` | the API |

Checked by mutation: unbounded pipes, a parallel stage never ordered, a failure that doesn't stop the other stages, each
of the two duplicate protections, the conversion precondition, a reject that doesn't free its slot, counters shared
between runs, the file header not skipped, the blacklist, reject counting: each makes at least one test fail.

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| **Simple** filters, testable one by one, **reusable** and reassemblable | For streaming jobs only, not for an interactive application |
| **Throughput**: all stages at the same time; parallelize the bottleneck, and only it | Throughput is that of the slowest; each pipe has a cost (copy, synchronization) |
| **Bounded memory** through back-pressure, whatever the data size | Stateful filters (deduplication, execution): single, and order to preserve |
| Rejects by reason, metrics per stage: you see where it's stuck | A failure halfway leaves partial work: idempotency mandatory to replay |

**When to use it**: file imports and exports, ETL, batch processing (statements, payroll, invoicing), processing of
event, media or log streams, compilers. Anywhere data goes through a sequence of independent steps. It is not a
full-fledged application architecture: a pipeline lives inside a service or a module, itself organized according to
another demo.
