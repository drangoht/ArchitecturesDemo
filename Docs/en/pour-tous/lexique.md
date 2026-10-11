# Short glossary

🏠 [Contents](README.md) · 🌐 [Français](../../pour-tous/lexique.md)

The words that keep coming back in these pages, explained simply.

---

## The basic words

**Application**: a program that provides a service: checking your accounts, booking a ticket, sending a message.

**Business rules** (or simply "the business"): what the application must know about its domain, independently of
computing. For a bank: "you can't transfer more than you have", "a transfer debits one account and credits another".
It is the most precious part: the one you want to protect.

**Data**: what the application must remember: the accounts, the balances, the history.

**Database**: the place where data is stored so it isn't lost when the computer is switched off. A kind of very fast
filing cabinet.

**Server**: a computer, often in a data center, that runs the application for others. Your phone asks it questions, it
answers.

**Interface** (or screen): what the user sees and touches: a web page, a mobile app, a window.

**Request**: a question or a demand sent to a server: "give me Alice's balance", "make this transfer".

**API**: an application's counter for other programs. It says which requests can be made and in what form. Like a
restaurant menu: you don't go into the kitchen, you order what is offered.

**Network**: what connects computers to each other. Fast, but not perfect: a message can arrive late, twice, or get lost.

---

## The words of organization

**Module**: a piece of an application that takes care of one subject (accounts, transfers, notifications), a bit like a
department in a company.

**Service** (or microservice): a module that has become a program of its own, which runs alone and is queried over the
network. Like a company department that moved into its own building.

**Dependency**: when one piece needs another to work. If the kitchen needs the storeroom, the kitchen "depends" on the
storeroom: reorganizing the storeroom forces the kitchen to change its habits.

**Deploy**: install a new version of the application so that users can use it. A "deployment" is a putting into service.

**Event**: an announcement that something just happened: "a transfer was made". Those interested listen and react; the
one announcing it doesn't necessarily know who is listening.

**Message**: a piece of information sent from one part of the system to another, like a letter. You can send it without
waiting for the reply.

**Test**: a small program that automatically checks that the application does what is expected. They are run again
after every change, like a checklist before an airplane takes off.

---

## The ideas that come back everywhere

**Idempotency**: being able to repeat a request without risk. Pressing the elevator call button five times doesn't bring
five elevators. Essential as soon as a message can arrive twice: a transfer sent twice must only debit once.

**Eventual consistency**: accepting that a piece of information may lag behind another, for a short moment. The bank
statement doesn't always show the payment made a minute ago, but it will end up showing it.

**Compensation**: cleanly undoing what was done when what follows fails. If the trip is cancelled after the hotel was
booked, you don't "erase" the booking: you cancel it, and get refunded.

**Outbox**: writing down in the same notebook, at the same moment, what you did AND the message you must send to say so.
If the mail carrier doesn't come, the message waits in the box: it won't be forgotten.

**Concurrency**: several people modifying the same thing at the same moment. Two simultaneous withdrawals from the same
account must not make one of them disappear. A referee is needed.

**Blast radius**: how far a failure does damage. A bulb burning out in one room, or the whole neighborhood plunged into
darkness.
