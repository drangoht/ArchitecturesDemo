# 2. Onion Architecture

> **In one sentence**: concentric layers; the domain is at the center, and **every dependency points toward it**.

📁 Code: [`02-Onion`](../../02-Onion) · ⬅️ [Hexagonal](01-hexagonale.md) · ➡️ [Clean](03-clean.md) ·
🌐 [Français](../02-onion.md)

---

## The idea

Proposed by **Jeffrey Palermo** in 2008. Same rule as hexagonal (the business depends on nothing), but with a
**center split into layers**: the domain model, then the domain services, then the application services.

```mermaid
flowchart TB
    subgraph Infra["Infrastructure / UI"]
        subgraph App["Application Services"]
            subgraph DS["Domain Services"]
                subgraph DM["Domain Model"]
                    Account["Account"]
                end
                SV["TransferService"]
                I["IAccountsRepository<br/>IUnitOfWork"]
            end
            AS["AccountsAppService<br/>TransfersAppService"]
        end
        EF["BankDbContext<br/>AccountsRepository"]
        Api["Endpoints + middleware"]
    end

    EF -. "implements" .-> I
    Api --> AS
    AS --> SV
    SV --> Account

    classDef c1 fill:#ddf4ff,stroke:#0969da
    classDef c2 fill:#fff8c5,stroke:#9a6700
    classDef c3 fill:#ffebe9,stroke:#cf222e
    classDef c4 fill:#dafbe1,stroke:#1a7f37
    class DM c4
    class DS c3
    class App c2
    class Infra c1
```

| Layer | Contents | Role |
|---|---|---|
| **Domain Model** | `Account` (account), exceptions | The entities and their invariants |
| **Domain Services** | `TransferService` (transfer service) + interfaces `IAccountsRepository`, `IUnitOfWork` | Rules that span several entities; what the domain needs |
| **Application Services** | `AccountsAppService`, `TransfersAppService`, DTOs | Orchestration: load, delegate, save |
| **Infrastructure / UI** | EF Core, API, middleware | Implements the interfaces, exposes the application |

### Dependencies between projects

```mermaid
flowchart LR
    Api["Onion.Api"] --> Application["Onion.Application"]
    Api --> Infrastructure["Onion.Infrastructure"]
    Infrastructure --> Application
    Application --> Domain["Onion.Domain"]
    Infrastructure -. "implements the interfaces of" .-> Domain
```

---

## The code, step by step

The code is quoted exactly as it is.

### 1. The domain service

A rule that concerns **two** accounts naturally belongs to neither of them.
([TransferService.cs](../../02-Onion/Onion.Domain/Services/TransferService.cs))

```csharp
public class TransferService
{
    public const decimal TransferLimit = 5_000m;

    public void Transfer(Account source, Account target, decimal amount)
    {
        if (source.Id == target.Id)
            throw new BusinessRuleException("Cannot transfer to the same account.");
        if (amount > TransferLimit)
            throw new BusinessRuleException($"A transfer cannot exceed {TransferLimit:0} €.");

        source.Withdraw(amount);
        target.Deposit(amount);
    }
}
```

### 2. The interfaces are declared at the center

The infrastructure (outer layer) implements them: dependencies point inward.
([IAccountsRepository.cs](../../02-Onion/Onion.Domain/Interfaces/IAccountsRepository.cs))

```csharp
// The data access interfaces are declared at the center;
// the infrastructure (outer layer) implements them: dependencies point inwards.
public interface IAccountsRepository
{
    Task<Account?> ById(Guid id);
    Task Add(Account account);
    Task<bool> IsEmpty();
}

/// <summary>Commits at once all the changes made during a use case.</summary>
public interface IUnitOfWork
{
    Task Save();
}
```

### 3. The application service orchestrates, without deciding

The rule (limit, same account, balance…) lives in the domain service; a single save for both accounts: all or nothing.
([TransfersAppService.cs](../../02-Onion/Onion.Application/TransfersAppService.cs))

```csharp
public class TransfersAppService(IAccountsRepository accounts, IUnitOfWork unitOfWork, TransferService transferService)
{
    public async Task<TransferResult> Transfer(Guid sourceId, Guid targetId, decimal amount)
    {
        var source = await accounts.GetOrThrow(sourceId);
        var target = await accounts.GetOrThrow(targetId);

        // The rule (limit, same account, balance…) lives in the domain service.
        transferService.Transfer(source, target, amount);

        // A single commit for both accounts: all or nothing.
        await unitOfWork.Save();
        return new TransferResult(AccountDto.From(source), AccountDto.From(target));
    }
}
```

### 4. The infrastructure implements the domain's interfaces

The `DbContext` **is** the unit of work; the mapping is described here, never through attributes in the domain.
([BankDbContext.cs](../../02-Onion/Onion.Infrastructure/BankDbContext.cs))

```csharp
public class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Account> Accounts => Set<Account>();

    public Task Save() => SaveChangesAsync();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The mapping is described here, not by attributes in the domain: the domain knows nothing of EF Core.
        modelBuilder.Entity<Account>(account =>
        {
            account.HasKey(c => c.Id);
            account.Property(c => c.Owner).HasMaxLength(100);
        });
    }
}
```

### 5. Domain errors translated once

([Program.cs](../../02-Onion/Onion.Api/Program.cs))

```csharp
// Domain errors are translated into HTTP once, by a middleware.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (AccountNotFoundException e)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = e.Message });
    }
    catch (BusinessRuleException e)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});
```

### The path of a transfer, from the outside to the center

```mermaid
sequenceDiagram
    actor Client
    participant API as Onion.Api
    participant AS as TransfersAppService
    participant R as IAccountsRepository
    participant SV as TransferService
    participant C as Account
    participant U as IUnitOfWork
    Client->>API: POST /transfers
    API->>AS: Transfer(source, cible, 250)
    AS->>R: GetOrThrow(source), (cible)
    AS->>SV: Transfer(source, cible, 250)
    SV->>SV: limit, same account?
    SV->>C: source.Withdraw(250)
    SV->>C: target.Deposit(250)
    AS->>U: Save() — only once
    AS-->>API: TransferResult
    API-->>Client: 200 OK
```

---

## The tests

The central layers are tested **without any test double**; the application layer with a fake repository / unit of work.
([TransfersAppServiceTests.cs](../../02-Onion/Onion.Tests/Application/TransfersAppServiceTests.cs))

```csharp
[Fact]
public async Task A_refused_transfer_is_not_saved()
{
    await Assert.ThrowsAsync<BusinessRuleException>(() => Service.Transfer(Alice, Bob, 1_000m));

    Assert.Equal(0, _unitOfWork.SaveCount);
}
```

And the golden rule is checked on every build ([ArchitectureTests.cs](../../02-Onion/Onion.Tests/ArchitectureTests.cs)):
the domain depends on nothing, the application only depends on the domain.

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| Pure, testable domain | Many projects and interfaces for a small application |
| Structured center: model / domain services / application | Sometimes blurry line between domain service and application service |
| Explicit unit of work (all or nothing) | Risk of an anemic model if the rules drift into the services |

**Hexagonal or Onion?** It is the same dependency rule. Hexagonal stresses the **ports and adapters** around the core,
Onion the **layers at the center**. In practice, many ".NET Clean" projects are actually Onion.
