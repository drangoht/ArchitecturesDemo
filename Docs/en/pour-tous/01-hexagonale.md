# 1. Hexagonal architecture ("ports and adapters")

**French name**: *Architecture hexagonale*

🏠 [Contents](README.md) · ⬅️ [Layers](00-couches.md) · ➡️ [Onion](02-onion.md) · 🔧 [Technical version](../01-hexagonale.md) · 🌐 [Français](../../pour-tous/01-hexagonale.md)

<p align="center"><img src="../../pour-tous/images/01.jpg" alt="Illustration: a laptop" width="480"></p>

> **In one picture**: a laptop. What matters is inside; around it, standard **sockets**. You plug in a screen, a mouse
> or a USB stick, and you can change them without opening the computer.

---

## The idea

You put **the rules at the center**, and forbid them from knowing the outside world. They don't know whether there is a
database, a website or a mobile app.

Instead, they define **sockets** (the "ports"): "I need someone who can find an account", "I need someone who can notify
the customer". Around them, you plug in **adapters**: one for the real database, one for sending emails, one for the
website.

It is the opposite of layers: it is no longer the kitchen adapting to the storeroom, it is the storeroom adapting to the
kitchen's needs.

## Why it's good

- **The rules are protected.** Changing the database or the screen doesn't touch them.
- **Tests become easy**: you plug in fake sockets (an in-memory database, a fake email sender) and check the rules on
  their own, in a split second.
- **Several entrances for the same core**: a website, a command-line application, a nightly batch use exactly the same
  rules.

## What it costs

- **More parts**: each socket and each adapter is one more element to write and understand.
- **Discipline**: nothing physically stops a developer in a hurry from bypassing a socket. Automatic checks are needed
  to remind them.

## When to choose it

- When the rules are rich and must outlive the tools around them.
- When you want to test a lot, and fast.
- When the application must talk to several outside worlds (web, files, other systems).

## In our bank

The core decides on the transfer without knowing where the accounts are stored. The same core is plugged into a website,
a console and a fake bank for the tests. The "storeroom" was changed without touching a single rule.
