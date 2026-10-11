# 24. MVC and MVVM (organizing the screen)

🏠 [Contents](README.md) · ⬅️ [REST, gRPC, GraphQL](23-rest-grpc-graphql.md) · 🔧 [Technical version](../24-mvc-mvvm.md) · 🌐 [Français](../../pour-tous/24-mvc-mvvm.md)

<p align="center"><img src="../../pour-tous/images/24.jpg" alt="Illustration: mail you send and receive, and a car's dashboard" width="480"></p>

> **In one picture**: MVC is **the mail**. You fill in a form, send it, and receive in return a new letter, all ready.
> MVVM is **a car's dashboard**: the needles move live, at every moment, without you having to ask anything.

---

## The idea

The other pages organize what lies behind the screen. This one organizes **the screen itself**. In both cases, three
roles are separated so as not to mix everything up:

- **the model**: the bank's rules and data, which know nothing about screens;
- **the view**: what you see, and nothing else;
- **an intermediary**, which makes the link. That is where the two approaches differ.

**MVC** (Model, View, Controller), on the web: with each click, the browser sends a request. The **controller** receives
it, consults the bank, picks the page to send back and fills it in. The page is built, sent, then forgotten. On the next
click, it starts again.

**MVVM** (Model, View, ViewModel), in desktop and mobile applications: the screen is a living object, the **ViewModel**,
which keeps everything that is on screen (what is typed, selected, displayed) and knows what to do when you click. The
view is **plugged** into it: every time a piece of information changes, the screen updates itself, like a needle on the
dashboard.

## Why it's good

- **MVC**: simple and robust; the server remembers nothing between two clicks; works everywhere, even on an old phone.
- **MVVM**: the screen **reacts while you type** (an error shows before you even send, the button lights up when
  everything is right); and all of the screen's behavior is checked **without a screen**, by automatic tests.

## What it costs

- **MVC**: you only discover your mistakes after sending the form; each step (a confirmation) is one more page.
- **MVVM**: more machinery; and a screen that lives long demands rigor (not showing an answer that arrived late, not
  submitting twice on a double click).

## When to choose it

It is mostly **the platform** that decides:

- **MVC** for a website whose pages are built by the server;
- **MVVM** for a desktop or mobile application, which stays open and reacts continuously.

## In our bank

The same transfer screen, built twice. In MVC, on the website: you send the form, you receive the answer; an amount that
is too high first goes through a confirmation page, then is refused by the bank. In MVVM, in a window: the same amount is
flagged as soon as you type it, and the button stays greyed out. A second, very simple view, from the keyboard, uses
exactly the same ViewModel as the window.
