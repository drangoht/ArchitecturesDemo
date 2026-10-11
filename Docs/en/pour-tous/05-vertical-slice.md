# 5. Vertical Slice (arranging by vertical slices)

🏠 [Contents](README.md) · ⬅️ [Functional Core](04-coeur-fonctionnel.md) · ➡️ [Package by Component](06-composants.md) · 🔧 [Technical version](../05-vertical-slice.md) · 🌐 [Français](../../pour-tous/05-vertical-slice.md)

<p align="center"><img src="../../pour-tous/images/05.jpg" alt="Illustration: a recipe book" width="480"></p>

> **In one picture**: a recipe book. Each card contains everything needed for a dish: the ingredients, the steps, the
> cooking. You don't put "all the cutting" in one chapter and "all the cooking" in another.

---

## The idea

The previous architectures arrange things by **technical floor**: all the reception together, all the rules together,
all the data together. To understand "withdraw money", you have to open a piece of each floor.

Vertical Slice does the opposite: things are arranged by **feature**. "Open an account", "look up an account",
"withdraw money", "make a transfer": each one is a slice that goes from the request down to the database, in the same
place.

Only what really serves several slices is shared.

## Why it's good

- **To understand a feature, you open a single place.**
- **Adding a feature means adding a card**, without touching the others. Less risk of breaking something elsewhere.
- **Each slice does things its own way**: a simple lookup stays simple, a delicate operation gets more care. No heavy
  procedure imposed on everyone.
- Very close to the way people talk about a product: by features, not by technical layers.

## What it costs

- **Repetition**: two cards can redo the same thing. You have to spot in time what deserves to be shared.
- The rules are less protected from the technology: each slice talks directly to the database.
- Without discipline, each slice invents its own style.

## When to choose it

- For an application that grows by adding features, with modest rules.
- When a team wants to deliver fast and often, one feature at a time.
- Inside a simple module of a larger system.

## In our bank

One file per action: open, look up, withdraw, transfer. To understand the withdrawal, you read a single file. The only
shared rule is "the balance must be enough", because the withdrawal and the transfer both need it.
