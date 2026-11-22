# 26. Mediator (the switchboard)

🏠 [Contents](README.md) · ⬅️ [Transaction Script and Active Record](25-transaction-script-active-record.md) · 🔧 [Technical version](../26-mediator.md) · 🌐 [Français](../../pour-tous/26-mediator.md)

<p align="center"><img src="../../pour-tous/images/26.jpg" alt="Illustration: a switchboard operator with a headset, at a company's reception" width="480"></p>

> **In one picture**: a company's **switchboard**. You don't call the person who will take care of you directly: you
> call the switchboard, which knows who does what, writes every call in its register, checks you have your file
> number, then puts the call through.

---

## The idea

In an application, each action (withdrawing money, making a transfer) is handled by a specific piece of code. Usually,
whoever asks for the action calls that piece of code directly, like dialing a colleague's direct line.

With a **mediator**, you always go through **the switchboard**. You send it a request ("withdraw €100 from Alice's
account"); it knows whom to hand it to, and along the way it does, **for every request**, the same little things:
write the call in the register, check the request is complete, and, when it's over, file the folder away.

The switchboard can also make **announcements**: "a transfer has just been made". Everyone interested hears it and
reacts (telling the recipient, flagging a large amount to the compliance department), without the announcer knowing
who listens.

## Why it's good

- The things to do **for every request** (writing down, checking, filing) are written **only once**, at the
  switchboard, instead of being repeated everywhere.
- Everyone does only their real job: whoever handles a withdrawal only takes care of the withdrawal.
- You add a reaction to an announcement without disturbing whoever made it.

## What it costs

- **You no longer see who answers**: reading the request, nothing tells you which office handles it; you have to look.
- If **nobody** is in charge of a type of request, you only find out at the time of the call.
- Announcements are made **during** the operation: if a single listener has a problem, the whole operation is cancelled.
- It is often mistaken for another idea, **CQRS** (two ways of storing the data, one to write and one to read). The
  switchboard stores nothing: it puts calls through.

## When to choose it

- When many different operations need the **same checks** and the **same tracking**.
- Not for a small application: a direct call is easier to follow.

## In our bank

Every operation goes through the switchboard. A €0 withdrawal is refused at the check, without bothering anyone; a
withdrawal that is too large goes all the way to the accounts office, which refuses it. The register shows both. A
€1,000 transfer triggers an announcement: Bob is told, and the compliance department receives an alert.
