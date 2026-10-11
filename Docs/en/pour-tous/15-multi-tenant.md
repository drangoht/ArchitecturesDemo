# 15. Multi-tenant (several customers, a single application)

🏠 [Contents](README.md) · ⬅️ [Orchestrated Saga](14-saga-orchestree.md) · ➡️ [Backend for Frontend](16-bff.md) · 🔧 [Technical version](../15-multi-tenant.md) · 🌐 [Français](../../pour-tous/15-multi-tenant.md)

<p align="center"><img src="../../pour-tous/images/15.jpg" alt="Illustration: an apartment building" width="480"></p>

> **In one picture**: an apartment building. A single building, a single boiler, a single caretaker; but each family has
> its apartment, locked. Nobody walks into the neighbor's place, and each one furnishes it as they like.

---

## The idea

Many online software products serve **several client companies** with a **single installation**: your work email, your
accounting tool, your booking software. Each customer is a **tenant** of the application.

The challenge: share everything (the application, the servers, sometimes the database) **without ever mixing** the
customers. Customer A must never see, or change, customer B's data. Each customer can also have its own settings: its
limits, its prices, its quotas.

The biggest customers, or those who pay for it, can have their own database: a separate house in the same residence.

## Why it's good

- **A single application to run and update** for all the customers: much cheaper.
- **A new customer is set up in a few seconds**, without deploying anything.
- **Each one keeps its rules** thanks to per-customer settings.

## What it costs

- **The slightest mistake can leak data** from one customer to another: it is the unforgivable fault. Automatic
  safeguards are needed, not just vigilance.
- **The noisy neighbor**: a customer who consumes enormously can slow everyone else down, unless each one is limited.
- **Updates affect everyone at the same time.**

## When to choose it

- For software sold **online, by subscription**, to many organizations.
- When running one installation per customer would become too expensive.

## In our bank

The application serves three banks. The same address returns Alice's account at €1,000, €3,000 or €25,000 depending on
the bank asking, without the bank's rules having to care: the sorting is done automatically, and a request that doesn't
say which bank it comes from is refused rather than showing everything. Two banks share a database, the third has its
own. A customer who is too greedy is slowed down, without slowing the others.
