# Microkernel or Microservices?

> **The difference in one sentence**: despite the shared prefix, the microkernel **extends** an application (plug-ins
> loaded into its process), microservices **split** it (separate processes). One answers "how to add variants?", the
> other "how to deploy parts separately?".

📁 Demos: [`13-Microkernel`](../../../13-Microkernel) · [`08-Microservices`](../../../08-Microservices) ·
📖 Pages: [Microkernel](../13-microkernel.md) · [Microservices](../08-microservices.md) ·
🗂️ Cheat sheets: [13](../fiches/13-microkernel.md) · [8](../fiches/08-microservices.md)

⬅️ [Microservices or Serverless?](10-microservices-vs-serverless.md) · ➡️ [Orchestrated Saga or Choreography?](12-saga-orchestree-vs-choregraphie.md) · 🌐 [Français](../../differences/11-microkernel-vs-microservices.md)

---

## What they share

Pieces that **don't reference each other** and only meet at runtime, through a contract; the ability to add, remove or
update a piece **without recompiling the others**.

## The differences point by point

| | Microkernel | Microservices |
|---|---|---|
| Question | adding **variants** (per customer, country, offer) without touching the core | **deploying and sizing** parts independently |
| Processes | **one**: the plug-ins run in the core's | one per service |
| The contract | an **SDK**: .NET interfaces (`IControleOperation`, `IRegleFrais`…) | **JSON** over the network |
| Who calls whom | the **core** calls the plug-ins, at the extension points it planned | each service calls whoever it wants, over HTTP or messages |
| Cost of a call | a method call | a network round trip, which can fail |
| Updating a piece | drop in a DLL: reloaded **at runtime** | redeploy a service |
| A faulty piece | the plug-in is set aside ("in error"); the operation is refused for safety | the service is unreachable; the others retry, compensate |
| Isolation | **no security isolation**: a plug-in can do anything | strong: separate process, network, database |
| Data | the core's; plug-ins receive **values** | one database per service |
| Main trap | changing an extension point breaks every plug-in; SDK to version | partial failures, idempotency, operations |

## Side by side

```mermaid
flowchart LR
    subgraph K["MICROKERNEL: 1 process"]
        direction TB
        N["Core"] -->|"extension points"| SDK["SDK"]
        P1["PlafondNocturne"] --> SDK
        P2["FraisDeVirement"] --> SDK
    end
    subgraph M["MICROSERVICES: N processes"]
        direction TB
        V["Transfers"] -->|"HTTP"| C["Accounts"]
        V -.->|"messages"| NO["Notifications"]
    end
    K ~~~ M
```

## In the demos' code

The code is quoted exactly as it is, French comments and messages included.

**Microkernel**: it is the core that decides where the extensions come in, and in which order (1. "check" extension
point: a single refusal is enough) ([NoyauBancaire.cs](../../../13-Microkernel/Mk.Noyau/NoyauBancaire.cs)):

```csharp
// 1. Point d'extension « contrôle » : un seul refus suffit.
foreach (var controle in plugins.Actifs<IControleOperation>())
{
    string? refus = null;
    if (!Proteger(controle, () => refus = controle.Verifier(operation)))
        return Resultat.Refus($"Le plugin {controle.Nom} est en erreur : opération refusée par sécurité.");
    if (refus is not null)
        return Resultat.Refus($"[{controle.Nom}] {refus}");
}
```

A plug-in is a class loaded from a DLL, in its own context, but **in the same process**
([ChargeurPlugins.cs](../../../13-Microkernel/Mk.Noyau/ChargeurPlugins.cs)):

```csharp
internal sealed class ContexteChargement(string cheminDll)
    : AssemblyLoadContext(Path.GetFileNameWithoutExtension(cheminDll), isCollectible: true)
```

**Microservices**: Transfers calls Accounts **across the network**, with everything needed to survive its failures
([Micro.Virements/Program.cs](../../../08-Microservices/Micro.Virements/Program.cs)):

```csharp
builder.Services.AddHttpClient("comptes", (sp, client) =>
        client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Services:Comptes"]!))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    });
```

What follows from this:

- a `FraisDeVirement` plug-in runs **inside** the core's operation; no network, no idempotency to plan for;
- a plug-in that loops or reads the disk can harm the whole process; a faulty service only harms itself;
- adding a fee rule to a microservice requires **redeploying** it; with the microkernel, **dropping in** a DLL.

## How to choose

- **Microkernel** for a product with a stable core and many variants: software configurable per customer, rule engine,
  tool extensible by third parties (VS Code, browsers, payroll).
- **Microservices** for independent teams, different scaling needs, separate release cycles.
- **Untrusted plug-ins** (written by strangers): neither as is; one separate process per plug-in is needed, which brings
  the microkernel closer… to microservices.

## Combining them

A microservice can very well be organized as a microkernel: the "Fees" service has a stable core and loads each
customer's price lists as plug-ins.

## Misconceptions

- *"Microkernel = small services"*: it is a single application, whose core is small.
- *"Plug-ins are isolated"*: their **loading** is isolated (separate, unloadable contexts), not their execution.
