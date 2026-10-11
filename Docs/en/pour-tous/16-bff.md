# 16. Backend for Frontend (one assistant per application)

🏠 [Contents](README.md) · ⬅️ [Multi-tenant](15-multi-tenant.md) · ➡️ [Cells](17-cell-based.md) · 🔧 [Technical version](../16-bff.md) · 🌐 [Français](../../pour-tous/16-bff.md)

<p align="center"><img src="../../pour-tous/images/16.jpg" alt="Illustration: a grand hotel with one butler per guest" width="480"></p>

> **In one picture**: a grand hotel with one butler per guest. The one serving the guest in a hurry fetches everything
> at once and brings the essentials on a small tray; the one serving the guest who has time presents everything, in
> detail, on a big table. The kitchens are the same for everyone.

---

## The idea

A bank often has **several applications** for its customers: a website, a mobile app. They have opposite needs: a big
screen and a good connection on one side, a small screen and a sometimes slow network on the other.

Rather than forcing the bank's services to satisfy everyone, each application gets **its own assistant** on the server,
its "backend" (hence the name: a backend for each "frontend", each application). This assistant:

- fetches **in one go**, from the various services, everything a screen needs;
- **tailors the response** to that screen: nothing superfluous for mobile;
- **keeps the sign-in secrets** on the server side, out of the browser's reach;
- **adapts** if a service doesn't answer, by showing the screen without that part.

## Why it's good

- **Faster applications**: a single round trip per screen instead of a dozen.
- **Each application team moves on its own**: it evolves its assistant without negotiating with the others.
- **More security** for the website, since the secrets never go into the browser.

## What it costs

- **One more piece per application**, to write and run.
- **Repetition** between the assistants.
- **The temptation to put the bank's rules in it**, which should stay in the services.

## When to choose it

- When **several very different applications** use the same services.
- When the screens need data from many services at once.

## In our bank

The website's dashboard asks its assistant for a single thing, and the assistant queries seven services at the same time.
The response for mobile is more than five times lighter. If the offers service is down, the dashboard still shows,
without the offers. And the assistant grants no special privileges: Alice cannot touch Bob's account.
