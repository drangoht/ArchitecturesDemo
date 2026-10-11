# 11. Domain-Driven Design (designing from the business)

🏠 [Contents](README.md) · ⬅️ [Event Sourcing](10-event-sourcing.md) · ➡️ [Serverless](12-serverless.md) · 🔧 [Technical version](../11-ddd.md) · 🌐 [Français](../../pour-tous/11-ddd.md)

<p align="center"><img src="../../pour-tous/images/11.jpg" alt="Illustration: an architect who spends time with the family before drawing the house" width="480"></p>

> **In one picture**: an architect who, before drawing the house, spends time with the family. They learn the family's
> words, its habits, what matters to it. Then they draw a house where each room bears the name the family gives it.

---

## The idea

It is not a way of arranging folders, but a **way of designing**. It comes down to a few principles:

- **Speak the business's language.** The developers and the bank's experts agree on a shared vocabulary, and that
  vocabulary appears as is in the application. If the advisors talk about the "daily withdrawal limit", so does the
  application.
- **Notions that protect themselves.** An invalid IBAN simply cannot exist in the application; you cannot add euros and
  dollars by mistake.
- **The same word doesn't mean the same thing everywhere.** For the department that keeps the accounts, an account has a
  balance and operations. For the department that watches for fraud, it is a case file with alerts. Rather than one huge
  model that tries to say everything, you split into **contexts**, each with its own vocabulary, and organize their
  exchanges.
- **Protect yourself from old systems**: when you have to talk to old software with a strange format, a translator keeps
  its quirks from getting in.

## Why it's good

- **Fewer misunderstandings** between those who know the business and those who write the application.
- **Safer rules**, since gross errors become impossible.
- **A good split**: the contexts say where to draw the boundaries between modules or services.

## What it costs

- **Business experts must be available**, often and for a long time.
- **A lot of care** for sometimes simple needs: for an application that only records cards, it is out of proportion.

## When to choose it

- When the business is **rich and complicated**, and it is what gives the application its value.
- To **decide how to split** a large system, before choosing between a modular monolith and microservices.

## In our bank

Two contexts: account keeping and fraud monitoring, each with its small dictionary. On the third transfer in ten minutes,
the monitoring suspects something and **asks** for the account to be blocked: it doesn't block it itself, that isn't its
role. And an old exchange-rate system, with a weird format, is translated at the entrance.
