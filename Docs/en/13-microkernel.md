# 13. Microkernel (plug-in architecture)

> **In one sentence**: a **minimal core** that defines **extension points**; the variable features are **plug-ins**
> loaded at runtime, which you add, remove or update without recompiling or restarting the core.

📁 Code: [`13-Microkernel`](../../13-Microkernel) · ⬅️ [Serverless](12-serverless.md) · ➡️ [Orchestrated Saga](14-saga-orchestree.md) ·
🌐 [Français](../13-microkernel.md)

---

## The idea

The model of VS Code, Eclipse, web browsers, rule engines or payroll software: the core does few things but does them
well, and everything that **varies** (by customer, country, commercial offer) is plugged in around it. None of the
previous demos dealt with this axis: **extensibility**.

```mermaid
flowchart TB
    subgraph Noyau["Core — Mk.Kernel"]
        direction LR
        N["BankingKernel<br/>accounts · debit · credit"]
        R["PluginRegistry"]
        C["PluginLoader"]
    end
    SDK["Mk.Contracts (SDK)<br/>IPlugin · IOperationCheck<br/>IFeeRule · IStatementFormat"]
    subgraph Plugins["plugins/ folder — loaded at runtime"]
        direction LR
        P1["NightLimit"]
        P2["Blocklist"]
        P3["TransferFees"]
        P4["CsvExport + QifExport"]
    end
    Noyau --> SDK
    Plugins --> SDK
    C -. "discovers and loads<br/>(reflection)" .-> Plugins

    classDef sdk fill:#dafbe1,stroke:#1a7f37
    class SDK sdk
```

**The core references no plug-in**, and the plug-ins only see the SDK (`Mk.Contracts`) of the core. The two sides only
meet at runtime.

### The three extension points

| Extension point | Role | Demo plug-ins |
|---|---|---|
| `IOperationCheck` | Can **refuse** an operation before it happens | `NightLimit` (withdrawals limited at night), `Blocklist` (blacklist: transfers to forbidden accounts) |
| `IFeeRule` | Adds **fees** | `TransferFees` (0.1%, between €0.20 and €5) |
| `IStatementFormat` | Produces a **statement** in a format | `CsvExport`, `QifExport` — **two extensions in a single DLL** |

---

## Solution layout

```
13-Microkernel/
├── Mk.Contracts/               the SDK: interfaces and exchanged data
├── Mk.Kernel/                  BankingKernel, PluginRegistry, PluginLoader
├── Mk.Host/                    HTTP API + watching of the plug-in folder
├── Mk.Plugins.TransferFees/    ┐
├── Mk.Plugins.NightLimit/      │  plug-in projects: built into plugins/{Name}/,
├── Mk.Plugins.Blocklist/       │  each with its plugin.json settings
├── Mk.Plugins.Exports/         ┘
├── Plugins.props               settings shared by the plug-ins
└── plugins/                    ← build output (not versioned), read by the core
```

---

## The code, step by step

The code is quoted exactly as it is, French comments and messages included.

### 1. The SDK: what the core and the plug-ins share

The comments explain the minimum core version (a plug-in too recent is refused on load) and `Initialize` (called once
on load, with the settings read from the plug-in's `plugin.json`).
([Contracts.cs](../../13-Microkernel/Mk.Contracts/Contracts.cs))

```csharp
public interface IPlugin
{
    string Name { get; }
    string Description { get; }

    /// <summary>Minimum version of the kernel the plugin needs. A plugin that is too recent is refused at load time.</summary>
    Version MinimumKernelVersion => new(1, 0);

    /// <summary>Called once at load time, with the settings read from the plugin's plugin.json.</summary>
    void Initialize(PluginContext context) { }
}

/// <summary>Can refuse an operation before it takes place.</summary>
public interface IOperationCheck : IPlugin
{
    /// <returns>null if the operation is allowed, otherwise the reason for the refusal.</returns>
    string? Check(ProposedOperation operation);
}
```

Plug-ins receive **values** (`ProposedOperation`, `AccountStatement`), never the core's internal objects.

### 2. A plug-in: a class that implements an extension point

([NightLimit.cs](../../13-Microkernel/Mk.Plugins.NightLimit/NightLimit.cs))

```csharp
public sealed class NightLimit : IOperationCheck
{
    private decimal _limit = 200m;
    private TimeOnly _start = new(22, 0), _end = new(6, 0);

    public string Name => "NightLimit";

    public string? Check(ProposedOperation operation)
    {
        if (operation.Type != OperationType.Withdrawal || operation.Amount <= _limit)
            return null;

        var time = TimeOnly.FromDateTime(operation.Date);
        var atNight = _start <= _end
            ? time >= _start && time < _end
            : time >= _start || time < _end;  // range spanning midnight
        return atNight ? $"Withdrawal limited to {_limit} € between {_start:HH\\:mm} and {_end:HH\\:mm}." : null;
    }
}
```

Its settings come from its own `plugin.json`, next to its DLL:

```json
{ "limit": "200", "start": "22:00", "end": "06:00" }
```

### 3. Building a plug-in… without bundling the SDK

([Plugins.props](../../13-Microkernel/Plugins.props))

```xml
<PropertyGroup>
  <EnableDynamicLoading>true</EnableDynamicLoading>
  <OutputPath>$(MSBuildThisFileDirectory)plugins\$(MSBuildProjectName)\</OutputPath>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="$(MSBuildThisFileDirectory)Mk.Contracts\Mk.Contracts.csproj" Private="false" ExcludeAssets="runtime" />
</ItemGroup>
```

`Private="false"` avoids copying `Mk.Contracts.dll` into the plug-in folder. Why this is vital: see the next step.

### 4. Loading each plug-in in its own context

The DLL is loaded from MEMORY, not from the file, so it isn't locked and can be rebuilt, replaced or deleted while the
application runs. And the crucial point: the contracts must come from the CORE (default context). If the plug-in loaded
its own copy of `Mk.Contracts`, its `IPlugin` would be ANOTHER type than the core's `IPlugin` (same name, different
assemblies), and nothing would work.
([PluginLoader.cs](../../13-Microkernel/Mk.Kernel/PluginLoader.cs))

```csharp
internal sealed class PluginLoadContext(string dllPath)
    : AssemblyLoadContext(Path.GetFileNameWithoutExtension(dllPath), isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(dllPath);

    /// Loaded from MEMORY and not from the file: the DLL is not locked,
    /// it can be rebuilt, replaced or deleted while the application is running.
    public Assembly LoadPlugin() => LoadFromStream(new MemoryStream(File.ReadAllBytes(dllPath)));

    protected override Assembly? Load(AssemblyName name)
    {
        // Crucial point: the contracts must come from the KERNEL (default context).
        // If the plugin loaded its own copy of Mk.Contracts, its IPlugin would be ANOTHER type
        // than the kernel's IPlugin (same name, different assemblies), and nothing would work.
        if (name.Name == typeof(IPlugin).Assembly.GetName().Name)
            return null;

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromStream(new MemoryStream(File.ReadAllBytes(path)));
    }
}
```

```mermaid
flowchart TB
    subgraph Defaut["Default context (the process)"]
        H["Mk.Host"] --> N["Mk.Kernel"] --> K["Mk.Contracts<br/>(a single copy)"]
    end
    subgraph A1["'Blocklist' context (unloadable)"]
        L["Mk.Plugins.Blocklist"]
    end
    subgraph A2["'Exports' context (unloadable)"]
        E["Mk.Plugins.Exports"]
    end
    L -->|"Load(Mk.Contracts) → null:<br/>delegated to the default context"| K
    E -->|"same"| K
```

A test checks it: after loading, `Mk.Contracts` is present **only once** in the process.

### 5. Discovering the extensions through reflection

```csharp
return assembly.GetExportedTypes()
    .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(t))
    .Select(t => PluginRegistry.Prepare((IPlugin)Activator.CreateInstance(t)!, name, settings, log))
    .ToList();
```

`Prepare` checks the required core version, then calls `Initialize` with the settings from `plugin.json`.

```mermaid
stateDiagram-v2
    [*] --> Incompatible : MinimumKernelVersion > core version
    [*] --> Faulted : exception in Initialize
    [*] --> Active : loaded and initialized
    Active --> Disabled : POST /admin/plugins/{name}/disable
    Disabled --> Active : POST /admin/plugins/{name}/enable
    Active --> Faulted : exception during a call
    Faulted --> Active : reactivation or reload
```

### 6. The core calls the extensions at the planned places

1. "Check" extension point: a single refusal is enough. 2. "Fees" extension point: each rule can add its own.
3. The heart of the business, which belongs to the core alone.
([BankingKernel.cs](../../13-Microkernel/Mk.Kernel/BankingKernel.cs))

```csharp
// 1. "Check" extension point: a single refusal is enough.
foreach (var check in plugins.Active<IOperationCheck>())
{
    string? refusal = null;
    if (!Protect(check, () => refusal = check.Check(operation)))
        return Result.Refused($"The plugin {check.Name} is faulted: operation refused for safety.");
    if (refusal is not null)
        return Result.Refused($"[{check.Name}] {refusal}");
}

// 2. "Fees" extension point: each rule can add its own.
// …

// 3. The heart of the business, which belongs to the kernel alone.
var totalFees = fees.Sum(f => f.Amount);
if (amount + totalFees > account.Balance)
    return Result.Refused($"Insufficient balance (fees included: {totalFees:0.00} €).");
```

```mermaid
sequenceDiagram
    actor Client
    participant N as BankingKernel
    participant C1 as NightLimit
    participant C2 as Blocklist
    participant F as TransferFees
    Client->>N: Transfer(Alice → Bob, 300)
    N->>C1: Check(operation)
    C1-->>N: null (not a withdrawal)
    N->>C2: Check(operation)
    C2-->>N: null (Bob isn't listed)
    N->>F: ComputeFee(operation)
    F-->>N: €0.30
    N->>N: sufficient balance? debit 300 + fees 0.30, credit 300
    N-->>Client: 200 { solde: 699.70, frais: 0.30 }
```

### 7. A faulty plug-in doesn't bring the core down

Its exception is caught, and the plug-in is put "in error" (so disabled) until it is reactivated or reloaded.

```csharp
/// A plugin must never bring the kernel down: its exception is caught, and the plugin is marked "faulted"
/// (hence disabled) until it is enabled again or reloaded.
private bool Protect(IPlugin plugin, Action call)
{
    try
    {
        call();
        return true;
    }
    catch (Exception e)
    {
        plugins.ReportError(plugin, e);
        log($"[{plugin.Name}] exception, plugin disabled: {e.Message}");
        return false;
    }
}
```

A safety choice (*fail-closed*): the current operation is **refused**, rather than executed without a check.

### 8. Hot reload

The registry is **replaced in one block**; the old contexts are then unloaded. `PluginWatcher`
([Endpoints.cs](../../13-Microkernel/Mk.Host/Endpoints.cs)) watches the folder and reloads 750 ms after the last change
(a build writes several files in a row).

```mermaid
sequenceDiagram
    actor Dev as Developer
    participant FS as plugins/ folder
    participant S as PluginWatcher
    participant C as PluginLoader
    participant R as PluginRegistry
    participant N as BankingKernel
    Dev->>FS: dotnet build Mk.Plugins.Blocklist (API running)
    Note over FS: DLL replaced: possible,<br/>it was read into memory
    FS-->>S: file events
    S->>S: wait for 750 ms of quiet
    S->>C: Reload(folder)
    C->>C: new contexts, new instances
    C->>R: Publish(new list) — atomic replacement
    C->>C: Unload() the old contexts
    N->>R: Active<IOperationCheck>() → new version
```

---

## The tests

| File | What is checked |
|---|---|
| [KernelTests.cs](../../13-Microkernel/Mk.Tests/KernelTests.cs) | The core alone, with plug-ins **written in the test** (no DLL): fees, faulty plug-in isolated, incompatible plug-in refused, no export without a format plug-in |
| [LoadingTests.cs](../../13-Microkernel/Mk.Tests/LoadingTests.cs) | The real DLLs: discovery, settings, **a single** `Mk.Contracts`, plug-in deleted then **context actually unloaded** |
| [ApiTests.cs](../../13-Microkernel/Mk.Tests/ApiTests.cs) | End to end: fees, blacklist, night-time limit depending on the time (adjustable clock), formats, hot disabling |

Deleting the plug-in folder is possible while the plug-in is loaded, since the DLL was read into memory:

```csharp
[Fact]
public void A_plugin_deleted_from_the_folder_disappears_and_its_context_is_unloaded()
{
    // …
    // Possible while the plugin is loaded: the DLL was read into memory, the file is not locked.
    Directory.Delete(Path.Combine(folder, "Mk.Plugins.Blocklist"), recursive: true);
    var unloaded = loader.Reload(folder);

    Assert.DoesNotContain("Blocklist", Names(registry));
    Assert.True(WaitForUnloading(unloaded), "The old load contexts were not released.");
}
```

The test project references the plug-ins with `ReferenceOutputAssembly="false"`: they are **built** before the tests,
but never linked as libraries.

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| Features added **without touching the core** or redeploying it | Designing good extension points is hard; changing them breaks every plug-in |
| **Hot** enabling / disabling, per customer or per offer | The SDK becomes a public contract to **version** carefully |
| Isolation: a faulty plug-in is set aside | Plug-ins run **in-process**: no security isolation (a malicious plug-in can do anything) |
| Third parties and external teams can extend the product | Debugging and integration tests are trickier; unloading is fragile if a reference lingers |

**When to use it**: products configurable per customer, rule engines, tools extensible by third parties, software whose
core is stable but whose variants are many. **Pitfalls**: copying the SDK into the plug-in folder (incompatible types),
holding a reference to a plug-in (unloading impossible), loading untrusted plug-ins (a separate process is then
needed). In .NET, libraries such as *McMaster.NETCore.Plugins* wrap this mechanism.
