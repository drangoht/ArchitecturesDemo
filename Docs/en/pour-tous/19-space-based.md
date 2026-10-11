# 19. Space-based architecture

**French name**: *Architecture spatiale*

🏠 [Contents](README.md) · ⬅️ [Actors](18-modele-acteurs.md) · ➡️ [Pipeline](20-pipeline.md) · 🔧 [Technical version](../19-space-based.md) · 🌐 [Français](../../pour-tous/19-space-based.md)

<p align="center"><img src="../../pour-tous/images/19.jpg" alt="Illustration: a supermarket on a Saturday afternoon" width="480"></p>

> **In one picture**: a supermarket on a Saturday afternoon. When the crowd arrives, checkouts are opened; when it
> leaves, they are closed. Each cashier has at hand everything needed to ring things up, without phoning the office for
> every item; the office does its accounts later, in peace.

---

## The idea

In almost every application, each request ends up going through **the database**. When the crowd arrives (sales, a
ticket office opening, contest results), it is the database that saturates, and everything slows down.

The space-based architecture takes the database off the path:

- each **processing unit** (a checkout) keeps **all the data in memory**, a full copy;
- the units keep each other informed of every change;
- the database is updated **in the background**, a little later;
- when the load rises, **units are added**, which start by copying another one's memory; when it drops, they are removed.

The name comes from the idea of a shared data "space" that all the units draw from.

## Why it's good

- **Very fast**: everything is in memory.
- **It withstands peaks**: units are added almost instantly.
- **The database can even go down for a while** without users noticing.

## What it costs

- **Collisions**: if two units modify the same account at the same instant, one can overwrite the other. To avoid it,
  the same customer must always be sent to the same unit, and even then, a window of risk remains when units are added
  or removed.
- **Everything must fit in memory**, which is expensive for large volumes.
- **The database lags behind** reality.
- A delicate infrastructure to run.

## When to choose it

- For **huge, unpredictable load peaks** that the database can't keep up with: ticket offices, auctions, flash sales.
- When a few rare collisions are acceptable, or avoidable through good routing.

## In our bank

The database is cut off: the bank keeps answering, and the writes wait their turn, then leave when the database comes
back. Six hundred simultaneous account openings take the bank from two to six units, then back down to two when things
calm down. And by routing customers any old way, you see the price of speed: accepted withdrawals that get lost.
