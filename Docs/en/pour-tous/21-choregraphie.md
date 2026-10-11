# 21. Choreography (everything through events, without a conductor)

**French name**: *Chorégraphie (événementiel pur)*

🏠 [Contents](README.md) · ⬅️ [Pipeline](20-pipeline.md) · ➡️ [Strangler Fig](22-strangler-fig.md) · 🔧 [Technical version](../21-choregraphie.md) · 🌐 [Français](../../pour-tous/21-choregraphie.md)

<p align="center"><img src="../../pour-tous/images/21.jpg" alt="Illustration: a troupe of dancers without a conductor" width="480"></p>

> **In one picture**: a troupe of dancers without a conductor. Each one knows their part and reacts to the others'
> movements: when one raises an arm, another leaps. Nobody leads, and yet the dance takes place.

---

## The idea

It is the counterpart of the [orchestrated saga](14-saga-orchestree.md), for the same multi-step transfer, but **without
an orchestrator**. Each service listens to the others' announcements and reacts by publishing its own:

- the accounts service announces "money reserved";
- compliance, hearing it, checks and announces "transfer authorized";
- the exchange service, hearing it, converts and announces "amount converted";
- and so on until the end.

Nobody knows the whole process: it **emerges** from everyone's reactions. To add a participant (a loyalty program that
gives points), it just has to listen to the right announcement, without changing anything for the others.

## Why it's good

- **The services are very independent**: none knows the others, only the announcements.
- **Adding a reaction is trivial**, without touching what exists.
- **No conductor** on whom everything depends.

## What it costs

- **The process is written nowhere.** To know "where does this transfer stand?", you have to rebuild the story from the
  announcements.
- **A failure is silence**: if a service doesn't react, nothing happens, and nobody complains. Timeouts and monitoring
  are needed to notice it.
- **Rollbacks are scattered**: each service must know by itself what to undo, and when.

## When to choose it

- For **reactions**: notify, update, compute points, archive. There, it is ideal.
- For an important process with precise failure rules, orchestration is generally preferred.
- The often-quoted rule: **orchestrate processes, choreograph reactions**.

## In our bank

The same transfer as the saga, with the same rules. A history shows each announcement, who published it, and in
response to what. The exchange service is put out of order: the transfer is left hanging, with no visible error, until
after thirty seconds the accounts service releases the reserved money by itself. The loyalty service was added
afterwards, without any other changing.
