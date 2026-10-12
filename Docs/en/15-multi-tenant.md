# 15. Multi-tenant (SaaS)

> **In one sentence**: a **single application**, a single deployment, serves **several customers** (the *tenants*);
> each one has its own data, rules and quotas, and none must ever see anyone else's.

📁 Code: [`15-MultiTenant`](../../15-MultiTenant) · ⬅️ [Orchestrated Saga](14-saga-orchestree.md) · ➡️ [BFF](16-bff.md) ·
🌐 [Français](../15-multi-tenant.md)

---

## The idea

Until now, each demo served **one** bank. A vendor selling its software to several banks has two options: install one
copy per customer (*single-tenant*), or run a single application for all of them (*multi-tenant*).

```mermaid
flowchart LR
    subgraph Mono["SINGLE-TENANT"]
        direction TB
        A1["Alpha Bank"] --> I1["Application v1.4<br/>+ its database"]
        B1["Beta Bank"] --> I2["Application v1.2<br/>+ its database"]
        C1["Gamma Bank"] --> I3["Application v1.5<br/>+ its database"]
    end
    subgraph Multi["MULTI-TENANT"]
        direction TB
        A2["Alpha Bank"] --> APP["Single application<br/>(one version, one deployment)"]
        B2["Beta Bank"] --> APP
        C2["Gamma Bank"] --> APP
        APP --> P[("Shared database<br/>Alpha + Beta")]
        APP --> D[("Dedicated database<br/>Gamma")]
    end
    Mono ~~~ Multi
```

| | Single-tenant (one installation per customer) | Multi-tenant |
|---|---|---|
| Update | N deployments, diverging versions | **one** deployment, everyone up to date |
| New customer | an installation to set up | one line in the catalog, **a few seconds** |
| Infrastructure cost | N times everything | pooled resources |
| Isolation | physical, natural | **to be built**: that is the whole subject of this demo |
| Demanding customer (performance, separate data) | natural | "dedicated database" plan, quotas |

The gain is economic and operational; the price is a **permanent risk**: a single badly filtered query, and one bank
sees another bank's accounts. All of the demo's code is there to make this mistake **impossible or noisy**.

---

## Where to store each customer's data?

| Model | Principle | Isolation | Cost per customer | In the demo |
|---|---|---|---|---|
| **Shared database** ("pool") | a `TenantId` column in each table, a filter on every query | logical (the code) | almost zero | **Standard** plan (Alpha, Beta) |
| **Schema per tenant** | one database, one SQL schema per customer | intermediate | low | — (can't be simulated in memory) |
| **Dedicated database** ("silo") | one database (one connection string) per customer | physical | high | **Premium** plan (Gamma) |

Most SaaS products are **hybrid**, as here: the shared database for the many, a dedicated database for the customers who
pay for it (volume, regulatory requirements, separate backup and restore). The same code serves both, because a single
class knows the difference. ([BankFactory.cs](../../15-MultiTenant/Mt.Bank/BankFactory.cs))

```csharp
public static string DatabaseName(Tenant tenant) =>
    tenant.Isolation == Isolation.DedicatedDatabase ? $"bank-{tenant.Id}" : SharedDatabase;
```

---

## The projects

```mermaid
flowchart LR
    API["<b>Mt.Api</b><br/>customer routes<br/>/platform back office"] --> BQ["<b>Mt.Bank</b><br/>BankingService (business)<br/>BankDbContext · BankFactory"]
    API --> LOC
    BQ --> LOC["<b>Mt.Tenants</b><br/>Tenant · Catalogue · Context<br/>resolvers · middleware · quotas"]
```

`Mt.Tenants` contains **no** banking logic: identifying the customer, knowing its plan, limiting its consumption —
it is the same code for any SaaS. `Mt.Bank` contains the business, written as if for a single bank.

---

## The path of a request

```mermaid
sequenceDiagram
    actor Client as Client (Beta Bank)
    participant M as TenantMiddleware
    participant Cat as Catalog
    participant L as Limiter (per tenant)
    participant S as BankingService
    participant DB as BankDbContext
    Client->>M: GET beta.localhost:5114/accounts/1111…
    M->>M: resolvers: subdomain = beta, header = (absent)
    M->>Cat: Find("beta")
    Cat-->>M: Beta Bank, Standard, active
    M->>M: TenantContext.Set(beta)
    M->>L: beta's quota: 60 / min
    L->>S: Get(1111…)
    S->>DB: Accounts.Where(c => c.Id == id)
    Note over DB: the global filter adds<br/>AND TenantId = 'beta'
    DB-->>S: Alice, €3,000
    S-->>Client: 200 (X-Tenant: beta)
```

The code is quoted exactly as it is, French comments and messages included.

### 1. Identifying the tenant, once, before anything else

The tenant is recognized by the **subdomain** (`beta.localhost:5114`, the usual SaaS choice) or by the `X-Tenant`
header (handy between services). The middleware invents nothing: absent → 400, unknown → 404, suspended → 403, and
**contradictory → 400** (subdomain "alpha" and header "beta": no choice is made, the request is refused).
([TenantMiddleware.cs](../../15-MultiTenant/Mt.Tenants/TenantMiddleware.cs))

```csharp
if (identifiers.Count > 1)
{
    // Subdomain "alpha" and header "beta": we don't pick one, we refuse.
    await Refuse(http, StatusCodes.Status400BadRequest,
        "Ambiguous tenant: " + string.Join(", ", found.Select(r => $"{r.Name} = {r.Id}")));
    return;
}
// …
context.Set(tenant);
```

> ⚠️ **In production**, a header is declarative: anyone can write `X-Tenant: gamma`. The tenant must come from the
> authenticated user's **token**, or be checked against it ("does this user belong to Gamma?"). The demo has no
> authentication, to stay focused on isolation.

### 2. The business never mentions the tenant

Not a single `where TenantId = …` in the service. The tenant is only used there to read **its rules**: same code,
different parameters. The target is looked up in the current tenant only: a transfer to a customer of another bank on
the platform is impossible by construction (that would be an interbank transfer, another process).
([BankingService.cs](../../15-MultiTenant/Mt.Bank/BankingService.cs))

```csharp
// Same code for all the banks, with each one's parameters.
if (amount > Rules.TransferLimit)
    throw new BusinessRuleException($"Transfer limit of {Tenant.Name}: {Rules.TransferLimit:0.00} €.");

// The target is looked up at the current tenant only: a transfer to a customer of another bank
// of the platform is impossible by construction (it would be an interbank transfer, another process).
var source = await Load(sourceId);
var target = await Load(targetId);
var fee = Rules.TransferFee;
```

| Bank | Plan | Database | Transfer limit | Transfer fee | Monthly fee | Max accounts |
|---|---|---|---|---|---|---|
| Alpha | Standard | `bank-shared` | €1,000 | — | €2 | 5 |
| Beta | Standard | `bank-shared` | €5,000 | €0.50 | — | 5 |
| Gamma | Premium | `bank-gamma` | €50,000 | — | €10 | 1,000 |

The rules come from each bank's **contract**; the plan (Standard / Premium) only sets the isolation and the technical
quotas. When customers want different **behaviors**, and not just parameters, you move on to the extension points of
the [Microkernel](13-microkernel.md).

### 3. The safeguard: the DbContext

This is where isolation is won or lost. Three protections, applied automatically to every entity implementing
`IBelongsToTenant`. The composite key lets two tenants have an account with the same identifier (here, Alice
and Bob exist everywhere) without conflict in the shared database.
([BankDbContext.cs](../../15-MultiTenant/Mt.Bank/BankDbContext.cs))

```csharp
private void Isolate<T>(ModelBuilder model) where T : class, IBelongsToTenant
{
    // Composite key: two tenants can have an account with the same identifier (here, Alice and Bob exist everywhere)
    // without conflict in the shared database.
    model.Entity<T>().HasKey(nameof(IBelongsToTenant.TenantId), "Id");
    model.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenant);
}
```

```mermaid
flowchart TD
    Q["Business LINQ query<br/><code>Accounts.Where(c => c.Id == id)</code>"] --> F["① global filter<br/><code>AND TenantId = @courant</code>"]
    F --> C{"Current<br/>tenant?"}
    C -->|"none"| X["NoTenantException<br/><b>closed by default</b>"]
    C -->|"beta"| R["Beta's rows only"]
    W["SaveChanges"] --> E{"Entity state"}
    E -->|"added, without a tenant"| S["② stamped with the current tenant"]
    E -->|"from another tenant"| Y["③ CrossTenantLeakException"]
```

- **① Global filter**: `CurrentTenant` is a property of the DbContext. The EF model (and its filter) is built only
  once for the whole application, but EF rereads this property on **every** query: each DbContext filters on its own
  tenant.
- **Closed by default**: without a tenant, the query **fails** instead of returning every bank. The platform, which
  legitimately needs to see everything, has to write it explicitly (without `IgnoreQueryFilters()`, these queries would
  throw `NoTenantException`):

  ```csharp
  // Without IgnoreQueryFilters(), these queries would throw NoTenantException.
  var accounts = await db.Accounts.IgnoreQueryFilters().Select(c => c.TenantId).ToListAsync();
  ```
- **② Stamping**: the business creates `new Account(...)` without ever setting `TenantId`.
- **③ Cross-tenant write**: an Alpha entity that mistakenly ended up in Beta's DbContext (shared cache, static
  variable…) makes `SaveChanges` fail. It is a bug, never a business case.
- **Composite key**: `(TenantId, Id)`. Alice has the same `Guid` in all three banks without conflict; and one
  tenant's row cannot "overwrite" another's.

### 4. The noisy neighbor

All the banks share the same servers. If Alpha floods the API, the others must not slow down: rate limiting is done
**per tenant**, with its plan's quota (60 requests/min in Standard, 600 in Premium). The quota is part of the key: after
a plan change, a new partition is created with the new quota (otherwise the old, cached one would stay in force).
([Setup.cs](../../15-MultiTenant/Mt.Tenants/Setup.cs))

```csharp
// The quota is part of the key: after a plan change, a new partition
// is created with the new quota (otherwise the old one, cached, would stay in force).
return RateLimitPartition.GetFixedWindowLimiter($"{tenant.Id}:{tenant.Quotas.RequestsPerMinute}", _ =>
```

Hence the order of the middlewares in `Program.cs`: **first** know who is calling, **then** apply their quota.

```csharp
// Order matters: first know WHO is calling, then apply THEIR quota.
app.UseTenants("/platform");
app.UseRateLimiter();
```

---

## The platform back office

The `/platform/…` routes work **on** the tenants, not **for** one of them: they run without a tenant, and it is the
only code that handles several of them. ([Platform.cs](../../15-MultiTenant/Mt.Api/Platform.cs))

| Operation | Route | Shared database (Standard) | Dedicated database (Premium) |
|---|---|---|---|
| Customer arrival | `POST /platform/tenants` | nothing to create | create the database |
| Suspension (unpaid…) | `POST …/{id}/suspend` | 403 on all its requests, data kept | same |
| Plan change | `POST …/{id}/plan` | copy its rows to the dedicated database, then erase them | the reverse |
| Departure (GDPR, end of contract) | `DELETE …/{id}` | delete **its** rows, table by table | delete **the database** |
| Inventory | `GET /platform/storage` | rows per tenant, database by database | |

A customer's departure shows the value of the dedicated database: a simple `DROP DATABASE`, easy to prove. In the
shared database, no table must be forgotten.

### Changing plans: moving data

```mermaid
sequenceDiagram
    participant P as Platform
    participant Cat as Catalog
    participant S as bank-shared
    participant D as bank-alpha
    P->>Cat: Alpha suspended (no more writes)
    P->>S: read Alpha's rows (global filter)
    P->>D: create the database, write the rows
    P->>S: erase Alpha's rows
    P->>Cat: Alpha: Premium, dedicated database, 600 req/min, reactivated
```

The old location is erased **only after** the copy has been saved, and the tenant is suspended during the operation:
no write can be lost between the two databases.

### A background job has no tenant

Outside an HTTP request, no middleware sets the tenant. The monthly fees job must do it itself, bank by bank, each in its
own injection scope (its DbContext, its rules):

```csharp
await using var scope = scopes.CreateAsyncScope();
scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
var charged = await scope.ServiceProvider.GetRequiredService<BankingService>().ChargeMonthlyFees(month);
```

If it forgot, the DbContext would throw `NoTenantException`: better a job that crashes than a job that charges
Alpha's fees to Gamma's customers.

---

## The pitfalls of multi-tenancy

| Pitfall | Consequence | Countermeasure in the demo |
|---|---|---|
| Forgetting the filter in a query | one bank sees the others' accounts | **global** filter, on every entity, checked by an architecture test |
| Query without a tenant (job, script) | everything is returned | **closed by default**: `NoTenantException` |
| Identifiers unique per customer only | collision in the shared database | composite key `(TenantId, Id)` |
| Trusting the header | impersonating another customer | in production: tenant taken from the token; here, contradictory values refused |
| Revealing that data exists | "this account exists at someone's" | 404, like a nonexistent account |
| Shared cache (`IMemoryCache`, Redis) | Alpha's response served to Beta | (not shown) prefix every key with the tenant |
| One customer consumes everything | the others slow down | **per-tenant** limiter, quotas per plan |
| Schema evolution | to be applied on **every** dedicated database | (not shown) migrations run tenant by tenant |

---

## The tests

| Test | What is checked |
|---|---|
| [IsolationTests.cs](../../15-MultiTenant/Mt.Tests/IsolationTests.cs) | |
| `The_same_identifier_designates_a_different_account_at_each_tenant` | composite key, shared database |
| `A_tenant_neither_sees_nor_touches_another_s_accounts` | cross-tenant read, list and withdrawal impossible |
| `Without_a_tenant_a_query_fails_instead_of_returning_everything` | closed by default; explicit `IgnoreQueryFilters` |
| `Saving_data_of_another_tenant_is_refused` | `CrossTenantLeakException` |
| `All_the_data_of_a_tenant_is_filtered_and_carries_the_tenant_in_its_key` | **architecture test**: every `IBelongsToTenant` entity |
| `The_business_rules_are_those_of_the_tenant`, `Le_quota_de_comptes…`, `Les_frais_mensuels…` | per-customer rules, idempotency |
| [ApiTests.cs](../../15-MultiTenant/Mt.Tests/ApiTests.cs) | |
| `A_request_without_a_clear_tenant_does_not_reach_the_business_code` | absent, unknown, contradictory, consistent |
| `A_noisy_neighbour_exhausts_its_quota_without_disturbing_the_others` | Alpha's 61st request: 429; Beta: 200 |
| `Moving_to_premium_moves_the_data_to_a_dedicated_database` | database migration, Premium quotas |
| `A_customer_leaving_erases_its_data_and_only_its_data` | offboarding in the shared database |
| `The_monthly_fee_task_works_tenant_by_tenant` | each bank's pricing, suspended one skipped, replay has no effect |

To make sure these tests really protect, you can remove the `HasQueryFilter` line from the DbContext: 11 tests fail.

---

## Summary

| 👍 Strengths | 👎 Limits |
|---|---|
| **One** deployment, **one** version for all customers | An isolation bug exposes **every** customer at once |
| A new customer in seconds, without installation | The "noisy neighbor": shared resources to throttle |
| Pooled costs; dedicated database reserved for those who pay | Schema migrations and backups to run on N dedicated databases |
| The business is still written for a single customer | Every request, job, cache, log must carry the tenant |

**When to use it**: as soon as you sell **the same software to several organizations** (B2B SaaS). Start with the
shared database and a global filter, and offer the dedicated database to the customers who need it. In production: the
tenant comes from authentication, the catalog is a separate database; **Finbuckle.MultiTenant** provides resolvers,
context and EF Core integration; on the database side, SQL Server's or PostgreSQL's *Row-Level Security* adds a second
barrier, below the application.
