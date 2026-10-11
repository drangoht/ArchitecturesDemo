# 22. Strangler Fig (replacing step by step)

🏠 [Contents](README.md) · ⬅️ [Choreography](21-choregraphie.md) · ➡️ [REST, gRPC, GraphQL](23-rest-grpc-graphql.md) · 🔧 [Technical version](../22-strangler-fig.md) · 🌐 [Français](../../pour-tous/22-strangler-fig.md)

<p align="center"><img src="../../pour-tous/images/22.jpg" alt="Illustration: renovating your house room by room, while still living in it" width="480"></p>

> **In one picture**: renovating your house room by room, while still living in it. You redo the kitchen, then the
> bathroom, then the bedrooms; the family moves from one room to another without ever sleeping outside. In the end,
> nothing of the old house is left.

---

## The idea

The name comes from a tropical tree, the strangler fig: it grows around an old tree, gradually wraps it, and ends up
replacing it completely.

All the other pages start from scratch. Here, you start from an **old system**, in service, which can be neither stopped
nor rewritten in one go. Rewriting everything in secret for two years, then switching over one Monday morning: that is
the scenario of the great IT disasters.

Instead:

1. you place a **reception** (a façade) in front of the old system: all users go through it;
2. you rewrite **one feature at a time** in the new system;
3. the reception sends that feature to the new system, the rest to the old one;
4. at the slightest problem, you **roll back** in an instant;
5. when nothing calls the old system anymore, you switch it off.

You can even test in **shadow mode**: the old system answers the users, the new one receives the same request behind the
scenes, and their answers are compared without taking any risk.

## Why it's good

- **No "big night"**: each step is small, reversible and immediately useful.
- **The risk is low**: you prove the new system behaves like the old one before entrusting it with the customers.
- **You can stop along the way** and keep a mix that works.

## What it costs

- **Two systems to keep alive together**, sometimes for a long time, and to keep synchronized: what one writes, the
  other must know.
- **The old system's habits must be reproduced**, quirks included, as long as others depend on them.
- **The risk of never finishing**: the last forgotten feature prevents switching the old system off.

## When to choose it

- To **replace an existing system** that can't be stopped. Almost always, in fact, as soon as it is in production.
- It is often how an application becomes modular, or splits into microservices: one feature after another.

## In our bank

An old system, written "the old way", and a new one. The reception says, in each response, who answered. The lookup has
already moved to the new system, the operations are in shadow mode, the transfers still on the old one; they are switched
over, then rolled back with a simple request. And the old system can only be switched off when every feature is migrated
and nobody calls it anymore: one small forgotten feature, the list of branches, is enough to prevent it.
