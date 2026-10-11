# 12. Serverless (no servers to manage)

🏠 [Contents](README.md) · ⬅️ [Domain-Driven Design](11-ddd.md) · ➡️ [Microkernel](13-microkernel.md) · 🔧 [Technical version](../12-serverless.md) · 🌐 [Français](../../pour-tous/12-serverless.md)

<p align="center"><img src="../../pour-tous/images/12.jpg" alt="Illustration: a taxi rather than a car" width="480"></p>

> **In one picture**: a taxi rather than a car. No purchase, no maintenance, no parking: you call when you need it, you
> pay per ride. But sometimes you have to wait for it to arrive, and with long trips every day, the car would have cost
> less.

---

## The idea

"Serverless" means "without servers"… to manage yourself. There are servers, but they belong to a provider (Amazon,
Microsoft, Google), who takes care of everything.

You no longer write an application that runs day and night, but **small functions**, each woken up by an event: a
request on the site, a file dropped off, a message received, a clock striking. The provider starts the function,
multiplies it if there are many requests, switches it off when there is nothing left to do, and **bills per use**.

## Why it's good

- **Nothing to install or maintain**: no machine to monitor, update, restart.
- **It grows on its own**: a thousand requests at once, and the provider starts a thousand copies of the function.
- **You only pay for what you use**: a little-used application costs almost nothing.

## What it costs

- **The cold start**: a function that was asleep takes a moment to wake up; the first user waits.
- **No memory between two calls**: everything to be remembered must be stored with the provider.
- **Everything can happen twice**: a message can be delivered twice, you have to protect yourself against it.
- **You depend on the provider**, its tools and its prices; changing provider takes work.
- For a **heavy, constant** activity, the bill can exceed that of a classic server.

## When to choose it

- For **irregular** processing: peaks from time to time, calm the rest of the time.
- For reactions to events: processing a file as soon as it arrives, sending a message, running a calculation at night.
- To start quickly, without a team to manage servers.

## In our bank

The provider is simulated, but it behaves like a real one: the first request waits for the function to wake up, the
following ones are fast. Dropping off a file of eight transfers wakes up eight copies of the function that executes a
transfer, at the same time. And a bill, at real prices, shows that a complete demonstration costs a few thousandths of a
cent.
