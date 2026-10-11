# 13. Microkernel (a minimal core and its extensions)

🏠 [Contents](README.md) · ⬅️ [Serverless](12-serverless.md) · ➡️ [Orchestrated Saga](14-saga-orchestree.md) · 🔧 [Technical version](../13-microkernel.md) · 🌐 [Français](../../pour-tous/13-microkernel.md)

<p align="center"><img src="../../pour-tous/images/13.jpg" alt="Illustration: a drill and its case of bits" width="480"></p>

> **In one picture**: a drill and its case of bits. The drill does only one thing, spin; depending on the bit you attach,
> it drills, screws or sands. You add bits without buying a new drill.

---

## The idea

You build a **minimal core** that does the essentials (keeping accounts, debiting, crediting) and provides **slots** where
**extensions** (also called "plug-ins") can be plugged in. Everything that varies from one customer to another, or from
one year to the next, becomes an extension: an extra check, a fee calculation, an export format.

It is the principle of your web browser and its extensions, of drawing software and its filters, of a game console and
its cartridges.

## Why it's good

- **The core stays small and stable**: it is rarely touched, so rarely broken.
- **An extension is added, removed or updated** without reinstalling the application, sometimes without even stopping it.
- **Each customer can have their extensions**: the same application, different behaviors.
- **Others can write extensions** (partners, other teams) by following the instructions published by the core.

## What it costs

- **The extensions' instructions are a commitment**: changing them breaks every existing extension.
- **A badly written extension** runs inside the application and can disturb it: you need to be able to set it aside.
- Choosing the slots well is hard: too few, and nothing can be extended; too many, and the core becomes complicated.

## When to choose it

- For a product sold to several customers who each want **their own rules**.
- When you want to let **others** enrich the application.
- When some rules change often (regulations, commercial offers).

## In our bank

The core keeps the accounts. Four extensions plug into it: a night-time limit, a blacklist of beneficiaries, transfer
fees, and statement exports in two formats. An extension can be disabled, or modified while the application is running:
it is reloaded on its own. An extension that crashes is set aside, without taking the rest down with it.
