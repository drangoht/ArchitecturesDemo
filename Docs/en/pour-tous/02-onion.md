# 2. Onion Architecture

🏠 [Contents](README.md) · ⬅️ [Hexagonal](01-hexagonale.md) · ➡️ [Clean Architecture](03-clean.md) · 🔧 [Technical version](../02-onion.md) · 🌐 [Français](../../pour-tous/02-onion.md)

<p align="center"><img src="../../pour-tous/images/02.jpg" alt="Illustration: an onion, or a fortified city" width="480"></p>

> **In one picture**: an onion, or a fortified city. The treasure is at the center; each wall protects it; you can go
> in toward the center, never the other way.

---

## The idea

It is a cousin of hexagonal, with a more precise arrangement. You draw **circles**:

- **at the center**, the bank's basic notions: an account, an amount, what a balance is;
- **around it**, the rules that make these notions work together: how a transfer is made;
- **then** what orchestrates a complete request;
- **outside**, everything technical: the database, the website.

The golden rule: **you only look inward**. The center knows nothing about the circles around it.

## Why it's good

- Same advantage as hexagonal: the rules depend neither on the database nor on the screen.
- **A clear arrangement** for large applications: each idea has its circle, you know where to put it.
- The split can be enforced by the very organization of the files: an inner circle simply cannot use an outer circle.

## What it costs

- **Many circles**, so many pieces, for a small application.
- A simple request goes through several layers before getting anywhere: a lot of ceremony for little.

## When to choose it

- For an application with many rules, which will live and grow for a long time.
- When the team likes strict arrangements, which the tools enforce on their own.

## In our bank

At the center, the account and the amount; around them, the transfer rule; outside, the database and the website. An
account doesn't know how it is saved, and doesn't need to know.
