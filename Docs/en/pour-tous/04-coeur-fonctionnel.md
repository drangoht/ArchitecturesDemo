# 4. Functional Core, Imperative Shell

🏠 [Contents](README.md) · ⬅️ [Clean Architecture](03-clean.md) · ➡️ [Vertical Slice](05-vertical-slice.md) · 🔧 [Technical version](../04-coeur-fonctionnel.md) · 🌐 [Français](../../pour-tous/04-coeur-fonctionnel.md)

<p align="center"><img src="../../pour-tous/images/04.jpg" alt="Illustration: the judge and the bailiff" width="480"></p>

> **In one picture**: the judge and the bailiff. The judge decides, on paper, without leaving the office. The bailiff
> fetches the documents before, and carries out the decision after.

---

## The idea

The application is split into two very different parts:

- **the core** (the judge): decisions only. You give it everything it needs to know ("Alice has €5,000, she has
  already transferred €1,500 today, she wants to transfer another €1,000, the daily limit is €2,000"), it answers with a
  decision ("accepted, here are the new balances" or "refused, daily limit exceeded"). It reads nothing, writes nothing,
  sends nothing: it thinks;
- **the shell** (the bailiff): everything that touches the real world. Fetching the accounts, calling the judge, then
  saving the decision and notifying people.

The core has a precious property: **with the same file, it always returns the same decision**. No surprises due to the
time of day, the database or the network.

## Why it's good

- **The rules are tested like a multiplication table**: this file, that decision. No need for a database, fake objects
  or staging.
- **Fewer strange bugs**: a decision cannot change because of a hidden side effect.
- Ideal for complex rules: limits, fees, calculations, conditions that follow one another.

## What it costs

- You have to **gather everything before deciding**. If the judge sometimes needs a document only known along the way,
  the organization becomes more acrobatic.
- A different way of thinking, which takes some getting used to for teams accustomed to "doing things as they go".

## When to choose it

- To isolate complicated, critical rules (financial calculations, pricing, eligibility).
- Often for one part of the application only, the one where the real puzzle lies.

## In our bank

The core receives the account, the amount and what Alice has already transferred today. It returns a decision. The shell
loads the account, calls the core, then saves. The rules are checked through dozens of cases, without any database.
