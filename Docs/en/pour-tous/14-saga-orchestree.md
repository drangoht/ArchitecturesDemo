# 14. Orchestrated Saga

**French name**: *Saga orchestrée*

🏠 [Contents](README.md) · ⬅️ [Microkernel](13-microkernel.md) · ➡️ [Multi-tenant](15-multi-tenant.md) · 🔧 [Technical version](../14-saga-orchestree.md) · 🌐 [Français](../../pour-tous/14-saga-orchestree.md)

<p align="center"><img src="../../pour-tous/images/14.jpg" alt="Illustration: a wedding planner" width="480"></p>

> **In one picture**: a wedding planner. She calls each supplier in order (venue, caterer, florist, photographer), notes
> where she stands in her notebook, chases those who don't answer. And if the wedding is called off, she calls each one
> back to cancel what had been booked.

---

## The idea

Some operations require the participation of several services, in several steps, sometimes long ones. An international
transfer, for example: reserve the money, check compliance, exchange the currency, credit, debit for good, notify the
customer.

A **saga** is that sequence of steps. It is **orchestrated** when a conductor, the **orchestrator**, drives it:

- it calls each participant in turn;
- it **records each step** in a notebook, so it can resume exactly where it was after a failure;
- it **retries** when a participant doesn't answer;
- it can **wait** for a human decision (a check for a large amount);
- and if it can't go all the way, it **undoes** what was done, step by step, in reverse order: that is called
  **compensation**.

## Why it's good

- **The process is written in a single place**: you can read it, show it, have it validated by the business.
- **You always know where each operation stands**, and why it stopped.
- **Failures are handled**: retries, timeouts, recovery, compensation.

## What it costs

- **The orchestrator is one more piece**, an important one, to run and monitor.
- **Each participant must accept being called twice** without doing things twice.
- **Some steps can't be undone**: an email sent stays sent. Past a certain point, there is no going back, and human
  intervention is sometimes needed.

## When to choose it

- For an **important, multi-step** process, between several services, with precise failure rules.
- When you must be able to answer "where does my request stand?".

## In our bank

A six-step transfer. Failures can be triggered: a transient failure of the exchange service is absorbed by two retries;
a lasting failure triggers compensation, and the reserved money is released. Above €10,000, the transfer waits for an
analyst's decision, and it is cleanly cancelled if nobody answers in time. Stop the orchestrator in the middle of a
transfer, then restart it: it resumes exactly where it was.
