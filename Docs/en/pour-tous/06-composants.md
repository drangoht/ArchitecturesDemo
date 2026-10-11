# 6. Package by Component (arranging by components)

🏠 [Contents](README.md) · ⬅️ [Vertical Slice](05-vertical-slice.md) · ➡️ [Modular Monolith](07-monolithe-modulaire.md) · 🔧 [Technical version](../06-composants.md) · 🌐 [Français](../../pour-tous/06-composants.md)

<p align="center"><img src="../../pour-tous/images/06.jpg" alt="Illustration: a company's departments" width="480"></p>

> **In one picture**: a company's departments. Each one has a reception desk where you drop off your requests; you don't
> walk into the offices to rummage in the cabinets. And the badge only opens the reception door.

---

## The idea

The application is split into big **subjects**: here, "the accounts" and "the transfers". Each subject becomes a
component with:

- **a single public entrance**, a reception desk that says what the component can do ("debit an account", "give a
  balance");
- **everything else locked**: its rules, its data, its internal arrangement.

The transfers department needs the accounts? It goes through their reception, like everyone else. And it is not just
an instruction: the tool that builds the application **refuses** any access to the inside of another component.

## Why it's good

- **The boundaries are guaranteed** by the tool, not just by the developers' goodwill.
- **Each component can change its inside** without warning the others, as long as its reception stays the same.
- **A component can be handed to a team**, which knows exactly what it must offer the others.

## What it costs

- You have to **design the receptions carefully**: whatever you put there, you'll have to keep for a long time.
- Since each component has its own data, an operation that touches two components (a transfer) is no longer
  automatically "all or nothing". If the second half fails, the first must be undone by hand.

## When to choose it

- When the application has well-separated subjects and is growing.
- When you want solid boundaries without multiplying the programs to install.
- As a first step toward the modular monolith, then possibly microservices.

## In our bank

Two components, each with its own database. The transfers never read the accounts' data: they ask the accounts'
reception to debit then to credit. If the credit fails, they give the money back to the starting account.
