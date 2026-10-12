# 15. Multi-tenant (SaaS)

> **En une phrase** : une **seule application**, un seul déploiement, sert **plusieurs clients** (les *locataires*) ;
> chacun a ses données, ses règles et ses quotas, et aucun ne doit jamais voir ceux des autres.

📁 Code : [`15-MultiTenant`](../15-MultiTenant) · ⬅️ [Saga orchestrée](14-saga-orchestree.md) · ➡️ [BFF](16-bff.md) · 🌐 [English](en/15-multi-tenant.md)

---

## L'idée

Jusqu'ici, chaque démo servait **une** banque. Un éditeur qui vend son logiciel à plusieurs banques a deux options :
installer une copie par client (*single-tenant*), ou faire tourner une seule application pour tous (*multi-tenant*).

```mermaid
flowchart LR
    subgraph Mono["SINGLE-TENANT"]
        direction TB
        A1["Banque Alpha"] --> I1["Application v1.4<br/>+ sa base"]
        B1["Banque Bêta"] --> I2["Application v1.2<br/>+ sa base"]
        C1["Banque Gamma"] --> I3["Application v1.5<br/>+ sa base"]
    end
    subgraph Multi["MULTI-TENANT"]
        direction TB
        A2["Banque Alpha"] --> APP["Application unique<br/>(une version, un déploiement)"]
        B2["Banque Bêta"] --> APP
        C2["Banque Gamma"] --> APP
        APP --> P[("Base partagée<br/>Alpha + Bêta")]
        APP --> D[("Base dédiée<br/>Gamma")]
    end
    Mono ~~~ Multi
```

| | Single-tenant (une installation par client) | Multi-tenant |
|---|---|---|
| Mise à jour | N déploiements, des versions qui divergent | **un** déploiement, tout le monde à jour |
| Nouveau client | une installation à monter | une ligne dans le catalogue, **quelques secondes** |
| Coût d'infrastructure | N fois tout | ressources mutualisées |
| Isolation | physique, naturelle | **à construire** : c'est tout le sujet de cette démo |
| Client exigeant (performances, données à part) | naturel | formule « base dédiée », quotas |

Le gain est économique et opérationnel ; le prix est un **risque permanent** : une seule requête mal filtrée, et une
banque voit les comptes d'une autre. Tout le code de la démo sert à rendre cette erreur **impossible ou bruyante**.

---

## Où ranger les données de chaque client ?

| Modèle | Principe | Isolation | Coût par client | Dans la démo |
|---|---|---|---|---|
| **Base partagée** (« pool ») | une colonne `TenantId` dans chaque table, un filtre sur chaque requête | logique (le code) | quasi nul | formule **Standard** (Alpha, Bêta) |
| **Schéma par locataire** | une base, un schéma SQL par client | intermédiaire | faible | — (non simulable en mémoire) |
| **Base dédiée** (« silo ») | une base (une chaîne de connexion) par client | physique | élevé | formule **Premium** (Gamma) |

La plupart des SaaS sont **hybrides**, comme ici : la base partagée pour le grand nombre, une base dédiée pour les clients
qui la paient (volumétrie, exigence réglementaire, sauvegarde et restauration séparées). Le même code sert les deux,
car une seule classe connaît la différence. ([BankFactory.cs](../15-MultiTenant/Mt.Bank/BankFactory.cs))

```csharp
public static string DatabaseName(Tenant tenant) =>
    tenant.Isolation == Isolation.DedicatedDatabase ? $"bank-{tenant.Id}" : SharedDatabase;
```

---

## Les projets

```mermaid
flowchart LR
    API["<b>Mt.Api</b><br/>routes des clients<br/>back-office /platform"] --> BQ["<b>Mt.Bank</b><br/>BankingService (métier)<br/>BankDbContext · BankFactory"]
    API --> LOC
    BQ --> LOC["<b>Mt.Tenants</b><br/>Tenant · Catalogue · Contexte<br/>résolveurs · middleware · quotas"]
```

`Mt.Tenants` ne contient **aucun** métier bancaire : identifier le client, connaître sa formule, limiter sa
consommation, c'est le même code pour n'importe quel SaaS. `Mt.Bank` contient le métier, écrit comme pour une seule banque.

---

## Le trajet d'une requête

```mermaid
sequenceDiagram
    actor Client as Client (Banque Bêta)
    participant M as TenantMiddleware
    participant Cat as Catalogue
    participant L as Limiteur (par locataire)
    participant S as BankingService
    participant DB as BankDbContext
    Client->>M: GET beta.localhost:5114/accounts/1111…
    M->>M: résolveurs : subdomain = beta, header = (absent)
    M->>Cat: Find("beta")
    Cat-->>M: Banque Bêta, Standard, active
    M->>M: TenantContext.Set(beta)
    M->>L: quota de beta : 60 / min
    L->>S: Get(1111…)
    S->>DB: Accounts.Where(c => c.Id == id)
    Note over DB: le filtre global ajoute<br/>AND TenantId = 'beta'
    DB-->>S: Alice, 3 000 €
    S-->>Client: 200 (X-Tenant: beta)
```

### 1. Identifier le locataire, une fois, avant tout le reste

Le locataire se reconnaît au **sous-domaine** (`beta.localhost:5114`, le choix habituel d'un SaaS) ou à l'en-tête
`X-Tenant` (pratique entre services). Le middleware n'invente rien : absent → 400, inconnu → 404, suspendu → 403,
et **contradictoire → 400**. ([TenantMiddleware.cs](../15-MultiTenant/Mt.Tenants/TenantMiddleware.cs))

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

> ⚠️ **En production**, un en-tête est déclaratif : n'importe qui peut écrire `X-Tenant: gamma`. Le locataire doit venir
> du **jeton** de l'utilisateur authentifié, ou être vérifié contre lui (« cet utilisateur appartient-il à Gamma ? »).
> La démo n'a pas d'authentification, pour rester centrée sur l'isolation.

### 2. Le métier ne parle jamais du locataire

Pas un seul `where TenantId = …` dans le service. Le locataire n'y sert qu'à lire **ses règles** : même code, paramètres
différents. ([BankingService.cs](../15-MultiTenant/Mt.Bank/BankingService.cs))

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

| Banque | Formule | Base | Plafond de virement | Frais de virement | Frais mensuels | Comptes max |
|---|---|---|---|---|---|---|
| Alpha | Standard | `bank-shared` | 1 000 € | — | 2 € | 5 |
| Bêta | Standard | `bank-shared` | 5 000 € | 0,50 € | — | 5 |
| Gamma | Premium | `bank-gamma` | 50 000 € | — | 10 € | 1 000 |

Les règles relèvent du **contrat** de chaque banque ; la formule (Standard / Premium) fixe seulement l'isolation et les
quotas techniques. Quand les clients veulent des **comportements** différents, et plus seulement des paramètres, on
passe aux points d'extension du [Microkernel](13-microkernel.md).

### 3. Le garde-fou : le DbContext

C'est ici que l'isolation se joue. Trois protections, appliquées automatiquement à toute entité qui implémente
`IBelongsToTenant`. ([BankDbContext.cs](../15-MultiTenant/Mt.Bank/BankDbContext.cs))

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
    Q["Requête LINQ du métier<br/><code>Accounts.Where(c => c.Id == id)</code>"] --> F["① filtre global<br/><code>AND TenantId = @courant</code>"]
    F --> C{"Tenant<br/>courant ?"}
    C -->|"aucun"| X["NoTenantException<br/><b>on ferme par défaut</b>"]
    C -->|"beta"| R["lignes de Bêta seulement"]
    W["SaveChanges"] --> E{"État de l'entité"}
    E -->|"ajoutée, sans locataire"| S["② estampillée avec le locataire courant"]
    E -->|"d'un autre locataire"| Y["③ CrossTenantLeakException"]
```

- **① Filtre global** : `CurrentTenant` est une propriété du DbContext. Le modèle EF (et son filtre) n'est construit
  qu'une fois pour toute l'application, mais EF relit cette propriété à **chaque** requête : chaque DbContext filtre sur
  son propre locataire.
- **Fermé par défaut** : sans locataire, la requête **échoue** au lieu de renvoyer toutes les banques. La plateforme, qui a
  légitimement besoin de tout voir, doit l'écrire explicitement :

  ```csharp
  // Without IgnoreQueryFilters(), these queries would throw NoTenantException.
  var accounts = await db.Accounts.IgnoreQueryFilters().Select(c => c.TenantId).ToListAsync();
  ```
- **② Estampillage** : le métier crée `new Account(...)` sans jamais renseigner `TenantId`.
- **③ Écriture croisée** : une entité d'Alpha arrivée par erreur dans le DbContext de Bêta (cache partagé, variable
  statique…) fait échouer `SaveChanges`. C'est un bogue, jamais un cas métier.
- **Clé composite** : `(TenantId, Id)`. Alice a le même `Guid` dans les trois banques sans conflit ; et une ligne
  d'un locataire ne peut pas « écraser » celle d'un autre.

### 4. Le voisin bruyant

Toutes les banques partagent les mêmes serveurs. Si Alpha inonde l'API, les autres ne doivent pas ralentir : la
limitation de débit se fait **par locataire**, avec le quota de sa formule (60 requêtes/min en Standard, 600 en Premium).
([Setup.cs](../15-MultiTenant/Mt.Tenants/Setup.cs))

```csharp
// The quota is part of the key: after a plan change, a new partition
// is created with the new quota (otherwise the old one, cached, would stay in force).
return RateLimitPartition.GetFixedWindowLimiter($"{tenant.Id}:{tenant.Quotas.RequestsPerMinute}", _ =>
```

D'où l'ordre des middlewares dans `Program.cs` : **d'abord** savoir qui appelle, **ensuite** appliquer son quota.

```csharp
// Order matters: first know WHO is calling, then apply THEIR quota.
app.UseTenants("/platform");
app.UseRateLimiter();
```

---

## Le back-office de la plateforme

Les routes `/platform/…` travaillent **sur** les locataires, pas **pour** l'un d'eux : elles s'exécutent sans
locataire, et c'est le seul code qui en manipule plusieurs. ([Platform.cs](../15-MultiTenant/Mt.Api/Platform.cs))

| Opération | Route | Base partagée (Standard) | Base dédiée (Premium) |
|---|---|---|---|
| Arrivée d'un client | `POST /platform/tenants` | rien à créer | créer la base |
| Suspension (impayé…) | `POST …/{id}/suspend` | 403 sur toutes ses requêtes, données conservées | idem |
| Changement de formule | `POST …/{id}/plan` | copier ses lignes vers la base dédiée, puis les effacer | l'inverse |
| Départ (RGPD, fin de contrat) | `DELETE …/{id}` | supprimer **ses** lignes, table par table | supprimer **la base** |
| Inventaire | `GET /platform/storage` | lignes par locataire, base par base | |

Le départ d'un client montre l'intérêt de la base dédiée : un simple `DROP DATABASE`, facile à prouver. En base partagée,
il faut n'oublier aucune table.

### Changer de formule : un déménagement de données

```mermaid
sequenceDiagram
    participant P as Platform
    participant Cat as Catalogue
    participant S as bank-shared
    participant D as bank-alpha
    P->>Cat: Alpha suspendue (plus aucune écriture)
    P->>S: lire les lignes d'Alpha (filtre global)
    P->>D: créer la base, écrire les lignes
    P->>S: effacer les lignes d'Alpha
    P->>Cat: Alpha : Premium, base dédiée, 600 req/min, réactivée
```

On n'efface l'ancien emplacement **qu'après** avoir enregistré la copie, et le locataire est suspendu pendant
l'opération : aucune écriture ne peut se perdre entre les deux bases.

### Une tâche de fond n'a pas de locataire

Hors requête HTTP, aucun middleware ne fixe le locataire. La tâche de frais mensuels doit le faire elle-même, banque par
banque, chacune dans son propre scope d'injection (son DbContext, ses règles) :

```csharp
await using var scope = scopes.CreateAsyncScope();
scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
var charged = await scope.ServiceProvider.GetRequiredService<BankingService>().ChargeMonthlyFees(month);
```

Si elle l'oubliait, le DbContext lèverait `NoTenantException` : mieux vaut une tâche qui plante qu'une tâche qui
prélève les frais d'Alpha sur les clients de Gamma.

---

## Les pièges du multi-tenant

| Piège | Conséquence | Parade dans la démo |
|---|---|---|
| Oublier le filtre dans une requête | une banque voit les comptes des autres | filtre **global**, sur toutes les entités, vérifié par un test d'architecture |
| Requête sans locataire (tâche, script) | tout est renvoyé | **fermé par défaut** : `NoTenantException` |
| Identifiants uniques par client seulement | collision dans la base partagée | clé composite `(TenantId, Id)` |
| Faire confiance à l'en-tête | usurpation d'un autre client | en production : locataire tiré du jeton ; ici, refus des valeurs contradictoires |
| Révéler l'existence d'une donnée | « ce compte existe chez quelqu'un » | 404, comme un compte inexistant |
| Cache partagé (`IMemoryCache`, Redis) | la réponse d'Alpha servie à Bêta | (non montré) préfixer toutes les clés par le locataire |
| Un client consomme tout | les autres ralentissent | limiteur **par locataire**, quotas par formule |
| Évolution du schéma | à appliquer sur **chaque** base dédiée | (non montré) migrations jouées locataire par locataire |

---

## Les tests

| Test | Ce qui est vérifié |
|---|---|
| [IsolationTests.cs](../15-MultiTenant/Mt.Tests/IsolationTests.cs) | |
| `The_same_identifier_designates_a_different_account_at_each_tenant` | clé composite, base partagée |
| `A_tenant_neither_sees_nor_touches_another_s_accounts` | lecture, liste et retrait croisés impossibles |
| `Without_a_tenant_a_query_fails_instead_of_returning_everything` | fermé par défaut ; `IgnoreQueryFilters` explicite |
| `Saving_data_of_another_tenant_is_refused` | `CrossTenantLeakException` |
| `All_the_data_of_a_tenant_is_filtered_and_carries_the_tenant_in_its_key` | **test d'architecture** : toute entité `IBelongsToTenant` |
| `The_business_rules_are_those_of_the_tenant`, `Le_quota_de_comptes…`, `Les_frais_mensuels…` | règles par client, idempotence |
| [ApiTests.cs](../15-MultiTenant/Mt.Tests/ApiTests.cs) | |
| `A_request_without_a_clear_tenant_does_not_reach_the_business_code` | absent, inconnu, contradictoire, concordant |
| `A_noisy_neighbour_exhausts_its_quota_without_disturbing_the_others` | 61e requête d'Alpha : 429 ; Bêta : 200 |
| `Moving_to_premium_moves_the_data_to_a_dedicated_database` | migration de base, quotas Premium |
| `A_customer_leaving_erases_its_data_and_only_its_data` | offboarding en base partagée |
| `The_monthly_fee_task_works_tenant_by_tenant` | tarif de chaque banque, suspendue ignorée, rejeu sans effet |

Pour s'assurer que ces tests protègent vraiment, on peut retirer la ligne `HasQueryFilter` du DbContext : 11 tests échouent.

---

## Bilan

| 👍 Points forts | 👎 Limites |
|---|---|
| **Un** déploiement, **une** version pour tous les clients | Une erreur d'isolation expose **tous** les clients à la fois |
| Nouveau client en quelques secondes, sans installation | Le « voisin bruyant » : ressources partagées à brider |
| Coûts mutualisés ; base dédiée réservée à qui la paie | Migrations de schéma et sauvegardes à faire sur N bases dédiées |
| Le métier reste écrit pour un seul client | Chaque requête, tâche, cache, journal doit porter le locataire |

**Quand l'utiliser** : dès qu'on vend le **même logiciel à plusieurs organisations** (SaaS B2B). Commencer par la base
partagée avec un filtre global, et proposer la base dédiée aux clients qui en ont besoin. En production : le locataire
vient de l'authentification, le catalogue est une base à part ; **Finbuckle.MultiTenant** fournit résolveurs, contexte et
intégration EF Core ; côté base, la *Row-Level Security* de SQL Server ou PostgreSQL ajoute une seconde barrière, en dessous
de l'application.
