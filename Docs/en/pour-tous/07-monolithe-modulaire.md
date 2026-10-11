# 7. Modular Monolith

**French name**: *Monolithe modulaire*

🏠 [Contents](README.md) · ⬅️ [Package by Component](06-composants.md) · ➡️ [Microservices](08-microservices.md) · 🔧 [Technical version](../07-monolithe-modulaire.md) · 🌐 [Français](../../pour-tous/07-monolithe-modulaire.md)

<p align="center"><img src="../../pour-tous/images/07.jpg" alt="Illustration: a shopping mall" width="480"></p>

> **In one picture**: a shopping mall. Each shop has its window, its till, its stock and its staff; they share the same
> roof, the same electricity and the same parking lot. A shop can open or close without the others moving out.

---

## The idea

"Monolith" means: **a single application**, installed in one block. The word has a bad reputation, because it is
associated with the plate of spaghetti where everything is mixed up. Here, it is the opposite: inside, the application is
divided into very independent **modules**.

Each module is a small, complete application: it has its own data, its own pages, its own rules. The modules talk to each
other in two ways:

- **by asking each other for something**, through a written contract ("debit this account and tell me if it worked");
- **by announcing what happened** ("a transfer just took place"), without knowing who is listening.

You can add a "notifications" module that listens to the announcements, without touching the others.

## Why it's good

- **The simplicity of a single application**: one installation, a single place to monitor, instant and reliable
  exchanges between modules.
- **The cleanliness of a strong split**: each team has its module, its data, its rules.
- **A door open to the future**: the day a module really needs to live on its own, it is ready to be taken out and become
  a microservice.

## What it costs

- **Everyone shares the same fate**: if the application goes down, all the modules go down; it is updated in one block.
- Since each module has its own data, an operation straddling two modules must plan how to recover if it fails.
- The boundaries must be held over time: the temptation to "just read a piece of the neighbor's data" is permanent.

## When to choose it

- **It is the recommended starting point** for most somewhat ambitious projects: you split things cleanly, without
  paying the price of microservices right away.
- When several teams work on the same application.

## In our bank

Three modules: accounts, transfers, notifications. The transfers ask the accounts to debit and credit, then announce
"transfer done". The notifications listen to that announcement and notify the customers. Removing the notifications
module takes one line, and nothing else breaks.
