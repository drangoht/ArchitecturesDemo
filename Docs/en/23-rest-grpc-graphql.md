# 23. REST, gRPC, GraphQL (API styles)

> **In one sentence**: the same bank exposed in three ways, as **resources** (REST), as **procedures** (gRPC) and as a
> **graph the client queries** (GraphQL); the business doesn't move, what changes is who decides the shape of the
> responses, what travels over the network, and what HTTP still does for you.

📁 Code: [`23-RestGrpcGraphQL`](../../23-RestGrpcGraphQL) · ⬅️ [Strangler Fig](22-strangler-fig.md) · ➡️ [MVC and MVVM](24-mvc-mvvm.md) ·
🌐 [Français](../23-rest-grpc-graphql.md)

---

## The idea: three presentations of the same business

The other demos change the system's **inside**. This one keeps a simple, single inside, the
[BankService](../../23-RestGrpcGraphQL/Styles.Bank/BankService.cs), and changes its **surface**: three APIs
in the same process, each a thin translation layer. An architecture test checks that the business knows nothing about
any of the three (`The_business_code_knows_nothing_about_API_styles`).

```mermaid
flowchart LR
    APP(["Mobile app<br/>(Styles.Client)"])
    subgraph SRV["Styles.Server"]
        direction TB
        R["REST<br/>:5123 · HTTP/1.1<br/>/customers, /accounts, /transfers"]
        Q["GraphQL<br/>:5123 · HTTP/1.1<br/>POST /graphql"]
        G["gRPC<br/>:5250 · HTTP/2<br/>bank.Bank/…"]
    end
    M["BankService<br/>(Styles.Bank)"]
    APP -->|"JSON"| R
    APP -->|"JSON"| Q
    APP -->|"Protobuf"| G
    R --> M
    Q --> M
    G --> M
```

| | **REST** | **gRPC** | **GraphQL** |
|---|---|---|---|
| You manipulate… | **resources** (nouns), with the HTTP verbs | a service's **procedures** (verbs) | a **graph** of types, which you traverse |
| The contract | implicit, or described separately (OpenAPI) | **banque.proto**, from which client and server are generated | the **schema** (SDL), checked on every request |
| Who chooses the response shape | the server, one representation per resource | the server, one message per method | **the client**, field by field |
| Format | JSON (text, field names) | Protobuf (binary, field numbers) | JSON, exactly the requested shape |
| Transport | HTTP, any version | **HTTP/2 required** (trailers) | HTTP, almost always `POST /graphql` |
| Errors | HTTP status + `problem+json` | 17 gRPC statuses, in the trailers | `200` + `errors`, next to the data obtained |
| HTTP cache | **yes** (GET, ETag, 304) | no | no (in practice) |
| The server pushes | no (you poll, or SSE) | native **streams** | subscriptions (WebSocket; not done here) |
| From a browser | yes | no (needs gRPC-Web and a proxy) | yes |
| Evolving | add fields, otherwise `/v2` | field numbers, unknown fields ignored | `@deprecated`, and you know who reads what |

---

## The components

- **The business** ([Styles.Bank](../../23-RestGrpcGraphQL/Styles.Bank)): customers, accounts, operations,
  transfers, and what the three APIs need without being specific to any of them: **single and batched** reads, a
  subscription to new operations, a counter of "database" reads.
- **The tooling** ([Styles.Tooling](../../23-RestGrpcGraphQL/Styles.Tooling)): since the packages aren't in the local
  NuGet cache, what Google.Protobuf, Grpc.AspNetCore and HotChocolate would provide, written by hand and reduced to the
  essentials, but faithful to the wire formats: [Protobuf.cs](../../23-RestGrpcGraphQL/Styles.Tooling/Protobuf/Protobuf.cs)
  (encoding), [Grpc.cs](../../23-RestGrpcGraphQL/Styles.Tooling/Grpc/Grpc.cs) (frames, statuses, deadlines),
  [GrpcHost.cs](../../23-RestGrpcGraphQL/Styles.Tooling/Grpc/GrpcHost.cs) and
  [GrpcClient.cs](../../23-RestGrpcGraphQL/Styles.Tooling/Grpc/GrpcClient.cs), and a mini GraphQL engine
  ([Parser.cs](../../23-RestGrpcGraphQL/Styles.Tooling/GraphQL/Parser.cs),
  [Schema.cs](../../23-RestGrpcGraphQL/Styles.Tooling/GraphQL/Schema.cs),
  [Engine.cs](../../23-RestGrpcGraphQL/Styles.Tooling/GraphQL/Engine.cs)). Writing these pieces shows what these
  protocols really are: a few hundred lines each.
- **The gRPC contracts** ([bank.proto](../../23-RestGrpcGraphQL/Styles.Contracts/bank.proto) and
  [Messages.cs](../../23-RestGrpcGraphQL/Styles.Contracts/Messages.cs)): without `protoc`, the classes are hand-written
  and a test checks that they match the `.proto` field by field.
- **The server** ([RestApi.cs](../../23-RestGrpcGraphQL/Styles.Server/RestApi.cs),
  [GrpcApi.cs](../../23-RestGrpcGraphQL/Styles.Server/GrpcApi.cs),
  [GraphQLApi.cs](../../23-RestGrpcGraphQL/Styles.Server/GraphQLApi.cs)) and the error table in
  [Translations.cs](../../23-RestGrpcGraphQL/Styles.Server/Translations.cs).
- **The comparison client** ([BankClients.cs](../../23-RestGrpcGraphQL/Styles.Client/BankClients.cs)): the same
  application written three times, behind an `IBankClient` interface, with a request and byte counter
  ([Measure.cs](../../23-RestGrpcGraphQL/Styles.Client/Measure.cs)). The tests use it too.

Data: **Alice** (`alice`) has three accounts, current account `10001`, Livret A (savings) `10002` and joint account
`10003`; **Bob** (`bob`) a current account `20001`.

---

## 1. The home screen: five requests, one, one

The mobile app shows, for each account, its label, its balance and its three latest operations. **Real run**
(`dotnet run --project Styles.Client`, with 50 ms of simulated latency per request, like a mobile network; output in
French: requests, bytes sent, bytes received):

```text
  REST     5 request(s), 0 B sent, 2014 B received, 178 ms
  GraphQL  1 request(s), 207 B sent, 876 B received, 60 ms
  gRPC     1 request(s), 14 B sent, 393 B received, 63 ms
```

**REST** follows the resources: the customer, their accounts, then the operations of **each** account ("1 + N"). The
last N leave in parallel, but that is three waves of round trips, and one request per account (the code is quoted
exactly as it is):

```csharp
var customer = await http.GetFromJsonAsync<JsonObject>($"/customers/{customerId}");
var accounts = await http.GetFromJsonAsync<JsonArray>($"/customers/{customerId}/accounts");
// 1 + 1 + N: one request per account for its operations (in parallel, but one request each).
```

**GraphQL** sends the description of the screen; the response has exactly its shape:

```graphql
query HomeScreen($id: ID!, $n: Int) {
  customer(id: $id) {
    name
    accounts { number label balance operations(latest: $n) { date label amount } }
  }
}
```

**gRPC** has a procedure tailored to this screen, declared in the contract:

```protobuf
rpc GetDashboard (DashboardRequest) returns (Dashboard);
```

```mermaid
sequenceDiagram
    participant A as App
    participant S as Server
    Note over A,S: REST (3 waves, 5 requests)
    A->>S: GET /customers/alice
    A->>S: GET /customers/alice/accounts
    par one per account
        A->>S: GET /accounts/10001/operations
        A->>S: GET /accounts/10002/operations
        A->>S: GET /accounts/10003/operations
    end
    Note over A,S: GraphQL or gRPC (1 request)
    A->>S: POST /graphql · or · bank.Bank/ObtenirTableauDeBord
```

The three resulting screens are **identical** (test `The_three_APIs_give_exactly_the_same_screen`). REST could have
done better, with a dedicated resource (`/customers/alice/dashboard`) or an `?expand=operations` parameter: that is
exactly what a [BFF](16-bff.md) does. But then you need one resource per screen, which GraphQL avoids.

---

## 2. What travels

For the same screen, REST receives **2.3 times** more than GraphQL and **5 times** more than gRPC (bodies only; test
`gRPC_transfers_the_fewest_bytes_and_REST_the_most`).

**REST** returns the full representation of each account, the same for every client. The screen wanted two fields; it
receives ten (over-fetching, test `The_representation_of_an_account_is_complete_and_the_same_for_all_clients`):

```json
{"number":"10001","label":"Current account","type":"current","iban":"FR76 3000 4000 0110 0010 0010 001","balance":1626.05,
 "currency":"EUR","openingDate":"2015-03-02","overdraftLimit":500,"owner":{"id":"alice","name":"Alice Martin"},
 "_links":{"self":"/accounts/10001","operations":"/accounts/10001/operations","owner":"/customers/alice"}}
```

**GraphQL** only returns the requested fields, but in JSON: each field name is repeated in every object. And it is the
one that **sends** the most (210 bytes): the query text leaves with every call. (In production, "persisted queries"
replace that text with an identifier.)

**gRPC** transmits neither names nor text: field numbers and binary integers. The example from the Protobuf
documentation (test `An_integer_is_encoded_as_a_varint_as_in_the_Protobuf_documentation`; field number 1, wire type 0
varint; 150 on 7 bits per byte, least significant first):

```text
field 1 = 150   →   08 96 01
                    08    = field number 1, wire type 0 (varint)
                    96 01 = 150, 7 bits per byte, least significant first
```

Two consequences visible in the contract: a default value (0, empty string) **doesn't travel at all**; and there is
**no decimal type**, hence amounts in cents:

```protobuf
// No decimal type in Protobuf: amounts travel in cents, on integers.
message Account {
  string number = 1;
  string label = 2;
  AccountType type = 3;
  int64 balance_cents = 4;
  repeated Operation latest_operations = 5;
}
```

GraphQL has no decimal either: `Float` is a double (`"Float" => JsonValue.Create(Convert.ToDouble(value))`). For money,
you usually define a custom scalar. And each format has its own precision for dates: the real run showed a GraphQL
screen different from the other two, because the schema dated to the second while REST kept the fractions. The business
therefore now dates its operations to the second:

```csharp
now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
```

---

## 3. N+1: moved, not removed

A single GraphQL query can hide **N** database reads: if the `operations` field is resolved account by account,
`client { accounts { operations } }` reads the database once per account, and
`client { accounts { owner { accounts { operations } } } }` nine times. The client sees only one round trip; the
database sees everything.

The engine therefore executes the query **level by level**: a field is resolved for all the objects of a level at once.
A **batched** resolver (the "DataLoader" of GraphQL engines) receives all the accounts and makes a single read:

```csharp
.BatchedField("operations", "[Operation!]!", (accounts, a) =>
{
    var byAccount = bank.LatestOperations(accounts.Select(c => c.Number).ToList(), a.GetOrDefault<int?>("latest") ?? 10);
    return accounts.Select(c => (object?)byAccount[c.Number]).ToList();
}, new Arg("latest", "Int", 10))
```

| For Alice's screen | REST | GraphQL | gRPC |
|---|---|---|---|
| HTTP requests | 5 | 1 | 1 |
| Database reads of the operations | **3** | **1** | **1** |

REST makes as many reads as requests: the N+1 is **visible** (in the access logs, in the latency). In GraphQL it is
**invisible** and depends on each resolver. gRPC, with its method built for the screen, settles it once and for all on
the server side. Tests `Behind_the_single_GraphQL_request_the_database_is_read_only_once` and
`The_operations_of_all_the_accounts_of_a_level_are_read_at_once` (9 accounts, one read).

---

## 4. One error, three languages

A transfer refused for insufficient balance, **real run**:

```text
  REST     422 INSUFFICIENT_BALANCE "Insufficient balance: 1160,01 € available (authorized overdraft: 300,00 €)."
  GraphQL  INSUFFICIENT_BALANCE   "Insufficient balance: 1160,01 € available (authorized overdraft: 300,00 €)."
  gRPC     FailedPrecondition     "Insufficient balance: 1160,01 € available (authorized overdraft: 300,00 €)."
```

The translation is written once, in [Translations.cs](../../23-RestGrpcGraphQL/Styles.Server/Translations.cs):

| Business error | REST | gRPC | GraphQL (`extensions.code`) |
|---|---|---|---|
| Unknown customer, account or transfer | `404 Not Found` | `NotFound` (5) | `UNKNOWN_ACCOUNT`… |
| Invalid amount, same account, invalid parameter | `400 Bad Request` | `InvalidArgument` (3) | `INVALID_AMOUNT`… |
| Insufficient balance | `422 Unprocessable Entity` | `FailedPrecondition` (9) | `INSUFFICIENT_BALANCE` |

- **REST**: the status gives the category, understood by every tool (caches, logs, alerts on 5xx); the
  `application/problem+json` body (RFC 9457) carries the details.
- **gRPC**: the HTTP response is **always 200**. The real status arrives at the end, in the HTTP/2 **trailers**
  (`grpc-status`, `grpc-message`), after the last message: that is why gRPC requires HTTP/2. The server refuses a call
  without trailers (`426`). The 17 statuses are categories, not business codes: to tell two `FailedPrecondition` apart,
  you need rich details (`google.rpc.Status`, not done here).
- **GraphQL**: `200` too, and the error in `errors`, with the **path** of the faulty field. The other fields are served
  (test `An_error_on_a_field_does_not_prevent_getting_the_others`):

```json
{"data":{"a":{"label":"Current account"},"b":null},
 "errors":[{"message":"Unknown account \"99999\".","locations":[{"line":1,"column":41}],"path":["b"],"extensions":{"code":"UNKNOWN_ACCOUNT"}}]}
```

When the field in error is **non-null** (`!`), it cannot be `null`: its parent becomes `null`, and so on up to the first
field that accepts it. With `operations(latest: 51)` (refused), the account, the list of accounts and finally `client`
are `null`, with one error per account (test `A_non_null_field_in_error_nulls_the_first_nullable_parent`).
Designing a schema also means choosing where these errors stop. Finally, an unexpected exception must disclose nothing:
`"Error inner."` (internal error), with no details (test `An_unexpected_error_discloses_nothing_to_the_client`).

---

## 5. Push rather than poll

**gRPC** can make the server answer several times: `FollowOperations` keeps the HTTP/2 response open and writes each new
operation into it, one frame at a time.

```csharp
await foreach (var operation in operations.ReadAllAsync(cancellation))
    await write(Message(operation));
```

The client sets a **deadline** (`grpc-timeout` header); the server enforces it too, and stops the work: the stream ends
with `DeadlineExceeded` and the subscription is released (test `The_client_deadline_also_stops_the_work_on_the_server_side`).
A client that leaves also frees the server (test `A_client_that_gives_up_frees_the_server`). **Real run** (server
stream, 1 request open for 2 s; three savings deposits arrive, made through each API; end of stream):

```text
  gRPC (server stream, 1 request open for 2 s):
      Savings via REST from 10002 (10,00 €)
      Savings via GraphQL from 10002 (10,00 €)
      Savings via gRPC from 10002 (10,00 €)
      end of stream: DeadlineExceeded
```

**REST** has no streams: you poll. But HTTP makes polling cheap: each account has an **ETag** that changes with every
operation; the client sends back the one it has (`If-None-Match`) and receives `304 Not Modified`, without a body, as
long as nothing has moved (test `The_ETag_avoids_transferring_again_an_account_that_has_not_changed`):

```text
  REST (polling with ETag, 5 times): 1 × 200, 4 × 304 Not Modified — 5 request(s), 0 B sent, 337 B received, 310 ms
```

That is REST's great asset, missing from the other two: GET is **safe and idempotent**, so everything between the client
and the server (browser, proxy, CDN) can cache. GraphQL sends everything through `POST /graphql` and loses that cache;
gRPC too. (To push, REST has *Server-Sent Events* or WebSockets; GraphQL, subscriptions over WebSocket: neither is done
here.)

---

## 6. Evolving the contract

All three styles let you add a field without breaking clients, each in its own way.

- **gRPC**: only the field **number** travels. You can rename a field; never change or reuse its number. An old client
  **skips** the fields it doesn't know, a new client reads default values in an old message (tests
  `An_old_client_ignores_the_fields_added_since`,
  `A_new_client_reads_an_old_message_with_the_default_values`). Hence the value 0 of each enum, reserved for
  "unknown" (`UNKNOWN_ACCOUNT_TYPE = 0`).
- **GraphQL**: the client only receives what it asks for, so adding is always safe. Removing is prepared: the
  `balanceText` field (the pre-formatted balance, from the first version) is **deprecated** in the published schema, and
  the server **counts who still asks for it** (`GET /graphql/usage`); it is removed the day the counter stops
  moving. No other style knows so precisely what its clients read.

```graphql
balanceText: String! @deprecated(reason: "Use 'balance' (a number) and format it for display.")
```

- **REST**: adding a JSON field is safe if clients ignore those they don't know (System.Text.Json's default behavior);
  for an incompatible change, you publish a new version (`/v2/accounts`, or a header), and keep both alive.

Without `protoc`, a trap lies in wait: classes that drift from the `.proto`. The test
`Each_message_of_the_proto_matches_its_class_field_by_field` compares the name, number and type of each field, and
`The_server_serves_each_method_of_the_service_and_nothing_else` the methods.

---

## 7. Protecting yourself from an overly curious client

In REST and gRPC, the server knows what each call costs: it wrote them. In GraphQL, **the client writes the query**:
`client { accounts { owner { accounts { owner { … } } } } }` can make the server work endlessly. The engine
therefore **validates** the whole query against the schema **before** executing anything (unknown fields and
arguments, required arguments, variable types, with their line and column), and refuses beyond 6 levels (tests
`A_query_that_is_too_deep_is_refused_before_being_executed`,
`An_unknown_field_or_argument_is_refused_by_the_validation_with_its_position`). In production you add a **maximum
cost** per query, limits on list sizes (here `latest` ≤ 50), and often an allowlist of known queries.

---

## The tests

49 tests ([Styles.Tests](../../23-RestGrpcGraphQL/Styles.Tests)): the three clients and the server in memory (the test
server handles HTTP/2 and trailers: gRPC works too), the GraphQL engine called directly, the Protobuf encoder alone.

| Test | What is checked |
|---|---|
| `The_three_APIs_give_exactly_the_same_screen`, `A_transfer_succeeds_the_same_way_through_the_three_APIs` | same business, same results |
| `REST_makes_one_request_per_account_GraphQL_and_gRPC_a_single_one_in_all`, `gRPC_transfers_the_fewest_bytes_and_REST_the_most` | network costs |
| `Behind_the_single_GraphQL_request_the_database_is_read_only_once` | N+1 in the database |
| `The_same_business_error_in_the_language_of_each_API` (3 cases) | error translations |
| `An_integer_is_encoded_as_a_varint_as_in_the_Protobuf_documentation`, `A_string_is_preceded_by_its_length`, `Default_values_do_not_travel`, `Zigzag_keeps_small_negatives_short`… | Protobuf encoding, byte by byte |
| `An_old_client_ignores_the_fields_added_since`, `A_new_client_reads_an_old_message_with_the_default_values` | gRPC contract evolution |
| `Each_message_of_the_proto_matches_its_class_field_by_field`, `The_enumeration_of_the_proto_matches_the_one_of_the_code`, `The_server_serves_each_method_of_the_service_and_nothing_else` | the `.proto` stays the source of truth |
| `A_unary_call_is_a_POST_whose_body_is_a_framed_message_and_the_status_in_the_trailers`, `A_business_error_still_answers_200_with_the_gRPC_status_in_the_trailers` | the gRPC wire format, seen "by hand" |
| `The_stream_pushes_each_operation_as_soon_as_it_happens`, `The_client_deadline_also_stops_the_work_on_the_server_side`, `A_client_that_gives_up_frees_the_server` | streams, deadlines, cancellation |
| `The_client_gets_exactly_the_requested_fields_and_nothing_else`, `Aliases_allow_asking_for_the_same_field_twice`, `The_variables_are_checked_according_to_their_type`, `A_mutation_is_executed_and_returns_what_the_client_asks_for` | GraphQL |
| `An_error_on_a_field_does_not_prevent_getting_the_others`, `A_non_null_field_in_error_nulls_the_first_nullable_parent`, `An_unexpected_error_discloses_nothing_to_the_client` | partial responses, `null` propagation |
| `The_published_schema_flags_the_deprecated_fields_and_the_server_knows_who_uses_them` | deprecation and usage statistics |
| `Errors_are_HTTP_statuses_with_a_problem_json_body` (4 cases), `A_transfer_creates_a_201_resource_whose_address_is_in_Location`, `The_ETag_avoids_transferring_again_an_account_that_has_not_changed` | REST |
| `The_business_code_knows_nothing_about_API_styles`, `The_tooling_ignores_the_bank_and_the_contracts_ignore_the_business_code` | **architecture tests** |

Checked by mutation (25 attempts, all detected): default values transmitted, unknown field refused, missing zigzag, a
field skip that reads the old position; a gRPC error returned as a success, deadline ignored, content type not checked,
trailers ignored by the client, a gRPC method reading account by account; a subscription never released, dates not
rounded; an unbatched GraphQL resolver, no depth limit, unknown field accepted, an error not absorbed by a nullable
field, null not propagated, internal error disclosed, an unused or wrongly typed variable accepted, missing usage
statistics; ETag ignored, 200 instead of 201; falsified error translations; a field number changed in a class without
touching the `.proto`.

Two of these guarantees come from real failures while writing the demo: skipping unknown fields
(`position += ReadVarint(bytes, ref position)` reads the old value of `position` **before** the call, and skips to the
wrong place: found by `An_old_client_ignores_the_fields_added_since`) and rounding dates (found in the real run,
section 2).

---

## Summary

| | 👍 Strengths | 👎 Limits |
|---|---|---|
| **REST** | Simple, universal, readable with `curl`; **the HTTP cache**; statuses understood by every tool | One shape per resource: too much data, or too many round trips; no streams; contract often implicit |
| **gRPC** | **Compact and fast**; strict, generated contract; streams both ways; propagated deadlines | HTTP/2 required, no browser without a proxy; binary unreadable without tooling; no HTTP cache |
| **GraphQL** | **The client chooses**: one round trip per screen, nothing superfluous; typed, introspectable schema; you know who reads what | Hidden N+1, costly queries to bound, HTTP cache lost, `200` for everything; an engine to operate |

**When to choose what**: **REST** for a public or partner API, simple resources, when caching matters; **gRPC** between
internal services (microservices, [demo 8](08-microservices.md)), for throughput, latency, streams and strict contracts
between teams; **GraphQL** in front of many varied clients with rich screens (mobile, web), often as an aggregation layer
on top of REST or gRPC services, where you would otherwise write one [BFF](16-bff.md) per application. The three often
coexist: gRPC between services, GraphQL or REST to the outside. In production: **ASP.NET Core** (Minimal API) and
**OpenAPI** for REST; **Grpc.AspNetCore**, **Grpc.Net.Client** and **Grpc.Tools** (`protoc`) for gRPC, with **gRPC-Web**
or JSON transcoding for browsers; **HotChocolate** or **GraphQL.NET** for GraphQL, with their DataLoaders, cost analysis
and persisted queries.
