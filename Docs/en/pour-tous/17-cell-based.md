# 17. Cell-based architecture

**French name**: *Architecture cellulaire*

🏠 [Contents](README.md) · ⬅️ [Backend for Frontend](16-bff.md) · ➡️ [Actors](18-modele-acteurs.md) · 🔧 [Technical version](../17-cell-based.md) · 🌐 [Français](../../pour-tous/17-cell-based.md)

<p align="center"><img src="../../pour-tous/images/17.jpg" alt="Illustration: identical, independent greenhouses, side by side" width="480"></p>

> **In one picture**: a row of identical greenhouses, each with its plants, its heating and its watering. If one's pane
> breaks on a frosty night, only its plants suffer; the other greenhouses notice nothing.

---

## The idea

Usually, when an application grows, you enlarge it: more servers, a bigger database. But then everyone shares the same
fate: a failure, or a botched update, hits **all** the customers.

The cell-based architecture does otherwise: it runs **several complete copies** of the application, the **cells**, and
each one serves **only part of the customers**. A small switchman at the entrance knows which cell each customer is in
and sends them there.

When all the cells are full, you **add a cell** rather than enlarging one. And a new version is first installed in **a
single** cell, which is watched, before moving on to the others.

## Why it's good

- **A failure only hits part of the customers.** That is the big advantage: the damage is limited.
- **Updates are cautious**: a faulty version is stopped in the first cell.
- **Each cell has a known size**, tested, under control: no bad surprises when growing.

## What it costs

- **Exchanges between cells are complicated**: a transfer between two customers in different cells becomes a remote
  operation, which must plan for its failures.
- **Everything is multiplied**: N copies of the application, N databases, N things to monitor.
- You have to know how to **move a customer** from one cell to another.

## When to choose it

- At **very large scale**, when a global outage costs more than several copies of the infrastructure.
- When customers are fairly independent of each other. That is how some services at Amazon or Slack work.

## In our bank

Three cells, each with a few customers. One of them is stopped: its customers get an error, the others notice nothing.
A version containing a bug is deployed: it is stopped in the first cell and rolled back, the other two never receive it.
And a customer can move from one cell to another, being unavailable only for an instant.
