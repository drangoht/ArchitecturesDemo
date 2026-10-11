# 3. Clean Architecture

🏠 [Contents](README.md) · ⬅️ [Onion](02-onion.md) · ➡️ [Functional Core](04-coeur-fonctionnel.md) · 🔧 [Technical version](../03-clean.md) · 🌐 [Français](../../pour-tous/03-clean.md)

<p align="center"><img src="../../pour-tous/images/03.jpg" alt="Illustration: a well-organized town hall" width="480"></p>

> **In one picture**: a well-organized town hall. One counter per procedure (identity card, building permit, wedding);
> each counter has its written procedure; and the answer is handed to you by a clerk who formats it for you.

---

## The idea

Popularized by Robert C. Martin ("Uncle Bob"), it takes the Onion's circles and adds two ideas:

- **One procedure, one counter.** Each thing the application can do (look up an account, make a transfer) is a separate
  "use case", with its input, its procedure and its output. By reading the list of counters, you know what the
  application does.
- **The result goes out through a presenter.** The counter doesn't choose how the answer will be displayed (web page,
  message, file). It gives the raw result; someone else takes care of presenting it.

## Why it's good

- **You read the application like a table of contents**: the list of its use cases says what it does.
- **Each procedure is tested on its own**, by checking what it handed to the presenter.
- **The rules are completely independent** of the technology, as in hexagonal and Onion.

## What it costs

- **Paperwork.** For a simple lookup, you need a counter, an input form, an output form and a presenter. That is a lot of
  elements for modest needs.
- The risk of following the method to the letter, even where it brings nothing.

## When to choose it

- For an application with many important procedures, which must last for years.
- When several teams work on it and need very clear common landmarks.

## In our bank

A "Look up an account" counter, a "Make a transfer" counter. The transfer counter doesn't build the web page: it
announces "transfer done, new balance €750" or "refused, insufficient balance", and a presenter turns it into a response
for the site. There are even two presenters for the lookup: one answers programs, the other prints a readable statement;
the counter itself didn't change.
