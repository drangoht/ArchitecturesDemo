# 20. Pipeline ("pipes and filters")

🏠 [Contents](README.md) · ⬅️ [Space-based](19-space-based.md) · ➡️ [Choreography](21-choregraphie.md) · 🔧 [Technical version](../20-pipeline.md) · 🌐 [Français](../../pour-tous/20-pipeline.md)

<p align="center"><img src="../../pour-tous/images/20.jpg" alt="Illustration: an assembly line" width="480"></p>

> **In one picture**: an assembly line. Each station does a single thing, always the same; a conveyor belt brings the
> parts to the next station. All the stations work at the same time, each on a different part.

---

## The idea

Some processing jobs consist of passing **a lot of data** through a sequence of steps: importing a file, transforming
data, producing reports. They are split into **filters** (workstations) connected by **pipes** (the conveyor belt).

Each filter:

- only knows what arrives on its belt and what it puts on the next one;
- does **a single thing**: read a line, check an account number, set duplicates aside, convert a currency…
- sets aside what's wrong, noting why.

The belts have a **limited length**: if a station is slow, the parts pile up in front of it, and the previous stations
slow down by themselves instead of overflowing.

## Why it's good

- **Each step is simple**, easy to understand and check on its own.
- **You recompose at will**: add, remove or reorder stations, or do a "dry run" without the last one.
- **Everything works at the same time**: while one station checks line 10, another executes line 9.
- **You spot the bottleneck** and double the slow station.
- **No overflow**: millions of lines go through without filling up memory.

## What it costs

- Reserved for **streaming** jobs, from step to step; not made for an interactive application.
- **Order sometimes matters**, and doubling a station can scramble it: it must be preserved.
- If the job stops halfway, you must be able to **resume** without redoing what is already done.

## When to choose it

- For **imports, exports, data transformations**, nightly jobs, continuous streams.
- Inside an application organized differently: it is a tool for one task, not for everything.

## In our bank

A company sends a file of transfers. Six stations: read, check the IBANs, set duplicates aside, convert currencies, check
for fraud, execute. The sample file gives three transfers and eight rejects, each with its reason. A hundred thousand
lines go through without ever keeping more than a few hundred in memory. The fraud check is the slow station; with eight
copies, the import goes five times faster, in file order.
