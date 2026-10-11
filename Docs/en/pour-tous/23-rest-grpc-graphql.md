# 23. REST, gRPC, GraphQL (three ways to talk to an application)

🏠 [Contents](README.md) · ⬅️ [Strangler Fig](22-strangler-fig.md) · ➡️ [MVC and MVVM](24-mvc-mvvm.md) · 🔧 [Technical version](../23-rest-grpc-graphql.md) · 🌐 [Français](../../pour-tous/23-rest-grpc-graphql.md)

<p align="center"><img src="../../pour-tous/images/23.jpg" alt="Illustration: three ways of ordering at a restaurant" width="480"></p>

> **In one picture**: three ways of ordering at a restaurant. **À la carte**: one dish per order, and you start again for
> dessert. **On a coded order slip**, between professionals: fast and unambiguous, provided you have the same notebook.
> **By ticking a list**: I tick exactly what I want, everything arrives at once.

---

## The idea

The other pages organize the inside of the application. This one looks at **how other programs talk to it** (a mobile
app, a website, a partner): its **API**, its counter.

- **REST** (the most widespread): the application presents **things** (a customer, their accounts, an account's
  operations), each at its own address. You ask for them one by one. Simple, understood by every tool, and the browser
  can keep the answers in memory so as not to ask again.
- **gRPC** (from Google): you call **actions** ("give me Alice's home screen"), in a binary, very compact format,
  described in a strict contract shared by both sides. Very fast, ideal between servers, but a browser can't speak it
  directly.
- **GraphQL** (from Facebook): the application is seen as a **network of connected information**, and it is the client
  who says **exactly** what it wants, in a single request: "Alice's name, her accounts, and the three latest operations
  of each".

## Why it's good

- **REST**: simple, universal, easy to test; answers can be cached.
- **gRPC**: very fast and light; strict contract; the server can send news continuously.
- **GraphQL**: one round trip per screen, nothing superfluous; ideal for applications with rich and varied screens.

## What it costs

- **REST**: too many round trips for a complex screen, or too much useless information.
- **gRPC**: unreadable without tools, not directly usable from a browser.
- **GraphQL**: harder to protect (a request can be very greedy) and to cache; one more engine to run.

## When to choose it

You choose **for each counter**, and the three are often combined:

- **REST** for a public API or for partners;
- **gRPC** between the internal services of a system;
- **GraphQL** in front of mobile and web applications with rich screens.

## In our bank

The same bank, offered three times. For Alice's home screen (three accounts and their latest operations), REST needs
five requests, GraphQL and gRPC just one. For exactly the same information, the REST response weighs about 2,000 bytes,
the GraphQL response 900, and the gRPC response less than 400.
