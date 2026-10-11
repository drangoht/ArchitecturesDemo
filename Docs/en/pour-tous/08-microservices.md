# 8. Microservices

🏠 [Contents](README.md) · ⬅️ [Modular Monolith](07-monolithe-modulaire.md) · ➡️ [CQRS](09-cqrs.md) · 🔧 [Technical version](../08-microservices.md) · 🌐 [Français](../../pour-tous/08-microservices.md)

<p align="center"><img src="../../pour-tous/images/08.jpg" alt="Illustration: a village of small shops, each in its own house" width="480"></p>

> **In one picture**: a village of small shops, each in its own house. They have their own opening hours, their staff,
> their stock. To work together, they phone each other and send each other parcels by courier.

---

## The idea

You take the modules of the modular monolith, and give each one **its own house**: a separate program, which runs alone,
with its own database. The services share nothing; they only talk to each other **over the network**, either by calling
("debit this account") or by sending messages ("a transfer took place").

A single entrance, the **gateway**, welcomes the customers and directs them to the right service, like the tourist
office at the entrance of the village.

## Why it's good

- **Each service lives its own life**: it can be updated, restarted or strengthened without touching the others.
- **Each team has its shop**, and can move at its own pace, with its own tools.
- **Only what needs it gets strengthened**: if transfers are in high demand, you add transfers, not all the rest.
- **A failure stays local**: if the notifications service goes down, the transfers carry on.

## What it costs

This is the big trap of this architecture: **everything that was easy becomes hard**.

- **The network isn't reliable.** A call can fail, arrive late, or succeed without the answer coming back. You have to
  retry, without ever debiting twice.
- **No more "all or nothing"**: if the debit succeeds and the credit fails, you have to give the money back yourself.
- **Information is sometimes late**: the notification arrives a few seconds after the transfer.
- **Many more things to monitor** and keep running, with failures that are harder to understand.

## When to choose it

- When **many teams** must work without getting in each other's way, or when some parts have very different power needs.
- When the team knows how to run and monitor a distributed system.
- **Rarely as a first choice**: you often get there from a modular monolith, module by module.

## In our bank

Four separate services (accounts, transfers, notifications, and a mail carrier for the messages), plus the gateway. The
transfers service retries when the accounts don't answer, without debiting twice. If the mail carrier is down, the
messages wait in an outbox; the notifications arrive as soon as it comes back.
