# 25. Transaction Script and Active Record (the starting point)

🏠 [Contents](README.md) · ⬅️ [MVC and MVVM](24-mvc-mvvm.md) · ➡️ [Mediator](26-mediator.md) · 🔧 [Technical version](../25-transaction-script-active-record.md) · 🌐 [Français](../../pour-tous/25-transaction-script-active-record.md)

<p align="center"><img src="../../pour-tous/images/25.jpg" alt="Illustration: a small village grocery, a single shopkeeper behind a wooden counter" width="480"></p>

> **In one picture**: the **small grocery** run by one single person. For each request, the grocer follows a
> **checklist**, from start to finish. Or else each item has its **index card**, which carries its information and its
> rules, and which the grocer puts back in the drawer after updating it.

---

## The idea

All the other pages of this series organize an application in a thought-out way. This one shows what you do **before**
thinking about organization: you write the rules directly, the shortest way. There are two ways to do it.

**Transaction Script** (the script of one operation): for each thing the application can do (withdraw money, make a
transfer), you write **one procedure**, from top to bottom, like a checklist: open the ledger, read the account, check
there is enough money, write the new balance, close the ledger. If something goes wrong along the way, you **put
everything back as it was**: nothing is half done.

**Active Record** (the active record): each account becomes a **card** that knows everything about itself: what it
holds (the owner, the balance), its rules ("you don't withdraw more than you have") and how to file itself in the
drawer. You take the card out, ask it to make the withdrawal, put it back.

## Why it's good

- **It's the simplest**: nothing to learn, nothing between the rules and the data. You see everything at a glance.
- **It's quick to write**: many websites are built this way, and many never needed more.
- **With the cards**, each rule is written **only once**, on the account's card.

## What it costs

- **With the checklists**, each list copies the checks it needs. The day a rule changes (a small overdraft is allowed),
  you have to find every list that contains it.
- **With the cards**, you must not forget to **put them back in the drawer**: a card updated but not filed is a lost
  change.
- In both cases, the rules are **glued to the ledger**: impossible to check them without it. When the application
  grows, this gluing gets in the way, and that is where the other architectures come in.

## When to choose it

- For a **small or simple** application: an internal tool, a management website, a prototype.
- To **start**, quite simply: you only leave it at the first real problem (rules copied everywhere, checks impossible
  without the database). Each of the other pages tells one of these ways out.

## In our bank

The same bank is written both ways, side by side, on **the same ledger**: a withdrawal made with the checklist shows up
on the card, and the other way round. A transfer to an account that doesn't exist is refused, and the money already
taken from Alice's account goes straight back: the procedure put everything back as it was.
