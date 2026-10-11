# 9. CQRS (separating writing and reading)

🏠 [Contents](README.md) · ⬅️ [Microservices](08-microservices.md) · ➡️ [Event Sourcing](10-event-sourcing.md) · 🔧 [Technical version](../09-cqrs.md) · 🌐 [Français](../../pour-tous/09-cqrs.md)

<p align="center"><img src="../../pour-tous/images/09.jpg" alt="Illustration: a restaurant kitchen and the specials board" width="480"></p>

> **In one picture**: a restaurant. In the kitchen, dishes are prepared following strict rules. In the dining room, a
> big board shows the day's specials and what's left, updated by a waiter as soon as they have time. Customers read the
> board; they don't go and count the plates in the kitchen.

---

## The idea

CQRS stands for *Command Query Responsibility Segregation*, which roughly means: **one organization for writing,
another for reading**.

Writing and reading don't have the same needs:

- **writing** (making a transfer, opening an account) means applying rules and never getting it wrong;
- **reading** (showing a statement, statistics) means quickly getting ready-made information, in very varied forms.

So the two are separated. On the writing side, the rules are applied. Every time something changes, it is announced. On
the reading side, these announcements are listened to and ready-to-display "cards" are prepared in advance: each
account's statement, the totals, the statistics.

## Why it's good

- **Reads are very fast**: everything is ready, it only has to be served.
- **Each side is simple**: the rules aren't cluttered by display needs, and the display can't break the rules.
- **A new display can be added** without touching the rules.
- Ideal when you read much more than you write (the case of most applications).

## What it costs

- **The board can lag a little.** Right after a withdrawal, the statement may still show the old balance, while the
  update goes through. That has to be acceptable for the users.
- **Duplicated information**, to keep up to date.
- More pieces to understand than a classic application.

## When to choose it

- When reads are very numerous, or very different from what is written (dashboards, statements, search).
- Often for **one part** of the application only, the one where reads are a problem.

## In our bank

The writing side checks the transfers and announces "money withdrawn", "transfer done". The reading side keeps
ready-made statements, with the balance after each operation. A two-second delay is set on purpose: right after a
withdrawal, you see the statement still at the old balance, then catching up.
