# 10. Event Sourcing (keeping the history rather than the result)

🏠 [Contents](README.md) · ⬅️ [CQRS](09-cqrs.md) · ➡️ [Domain-Driven Design](11-ddd.md) · 🔧 [Technical version](../10-event-sourcing.md) · 🌐 [Français](../../pour-tous/10-event-sourcing.md)

<p align="center"><img src="../../pour-tous/images/10.jpg" alt="Illustration: an old-fashioned account book" width="480"></p>

> **In one picture**: an old-fashioned account book. You don't write "I have €750 left" after erasing the previous
> figure: you write down each operation, line after line, in ink. The balance is recomputed by adding them up.

---

## The idea

Almost all applications remember **the current state**: "Alice has €750". How we got there is forgotten, or written down
elsewhere.

Event Sourcing does the opposite: it remembers **the list of everything that happened**, in order: "account opened",
"€1,000 deposited", "€250 transferred to Bob". A line is never changed, none is ever erased: lines are added. The current
state is now just a **calculation**: you replay the history, and get the balance.

A mistake? You don't erase it: you write a correction, as in accounting.

## Why it's good

- **A complete, reliable history**, for free: you know who did what, and when. Precious for a bank, accounting, logistics
  or anything that must be audited.
- **Time travel**: you can recompute the balance as it was on March 3 at noon.
- **New questions about the past**: a statistic invented today ("the largest withdrawals") can be computed over the whole
  history, since day one.

## What it costs

- **Another way of thinking**, which takes teams time.
- **Nothing is erased**, which is a problem with the right to be forgotten: you have to plan how to make the personal data
  of a departing customer unreadable.
- **Formats evolve**: an operation recorded five years ago must still be readable today.
- To display things quickly, ready-to-read cards are needed alongside (often with CQRS).

## When to choose it

- When **the history is itself precious information**: banking, insurance, accounting, parcel tracking.
- For one part of the application, rarely for all of it.
- To be avoided for an application that only records and displays simple cards.

## In our bank

The whole bank fits in a readable file, one line per event, never modified. Everything else can be deleted: on restart,
the balances and statements are recomputed from these lines. And you can ask for an account's state "as it was" at any
moment.
