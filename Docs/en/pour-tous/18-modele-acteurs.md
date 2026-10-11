# 18. The actor model

**French name**: *Modèle d'acteurs*

🏠 [Contents](README.md) · ⬅️ [Cells](17-cell-based.md) · ➡️ [Space-based](19-space-based.md) · 🔧 [Technical version](../18-modele-acteurs.md) · 🌐 [Français](../../pour-tous/18-modele-acteurs.md)

<p align="center"><img src="../../pour-tous/images/18.jpg" alt="Illustration: a post office where each clerk has their own mailbox and their own files" width="480"></p>

> **In one picture**: a post office where each clerk has their own mailbox and their own files. They handle their mail
> **one letter at a time**, and nobody else touches their files. If a clerk falls ill, a replacement takes their place
> and picks up the files where they were filed.

---

## The idea

The classic nightmare: two requests arrive **at the same time** on the same account. Each one reads "€1,000", each one
withdraws, and one of the two operations disappears. The usual solutions (locks, transactions) are delicate and slow
everything down.

The actor model takes the problem the other way around: each account is an **actor**, a small character who:

- **owns its own state** (its balance), which nobody else can touch;
- only receives **messages** ("withdraw €3", "give me your balance");
- handles them **one at a time**, in order of arrival.

No more jostling possible: there are never two hands in the same drawer. And when an actor crashes, a **supervisor**
replaces it with a fresh one, which rereads the saved state.

## Why it's good

- **Concurrency becomes simple**: each actor reasons as if it were alone in the world.
- **Millions of actors** can exist; those not in use are put to sleep, and woken up by the first message.
- **Failures stay local**: an actor that crashes is replaced, the others carry on.

## What it costs

- **No "all or nothing" between two actors**: a transfer becomes a conversation (debit, then credit, and give the money
  back if what follows fails).
- **Cross-cutting questions are hard**: "the list of all accounts" requires asking each actor.
- **Two actors waiting for each other** can get stuck.

## When to choose it

- When you have **many independent objects**, often modified at the same time: accounts, players of an online game,
  connected objects, carts.
- It is the model behind WhatsApp's messaging (Erlang) and Halo's game servers (Orleans).

## In our bank

One actor per account, one actor per transfer. A thousand €3 withdrawals arrive **at the same time** on Alice's account
(€1,000): exactly 333 are accepted, €1 is left, and the balance is never negative. An actor is made to crash at the worst
moment: it is replaced, and the half-done withdrawal simply never happened.
