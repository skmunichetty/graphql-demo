# Architecture

## Service boundaries

| | Estimates service | Accounting Integration service |
|---|---|---|
| Owns | Estimates, line items, pricing, status | Export records, provider integrations |
| Database | `estimates` (role `estimates_app`) | `accounting` (role `accounting_app`) |
| Knows about the other service through | — | The Estimates **GraphQL API** only |
| Deployable unit | `src/Estimates/Estimates.Api/Dockerfile` | `src/Accounting/Accounting.Api/Dockerfile` |

The services share **no domain code and no tables**. Locally both databases live in one PostgreSQL
container, but each database has its own owner role, and `REVOKE CONNECT … FROM PUBLIC` stops one
service's role from connecting to the other's database. The Accounting service keeps its own small
model of an estimate (`EstimateSnapshot`) containing only the fields it reads through the API.

`src/ServiceDefaults` is shared, but it contains technical plumbing only: JWT validation setup,
correlation IDs, OpenTelemetry, GraphQL limits and error handling, CORS, rate limiting and health
endpoints. Sharing it avoids copying security-sensitive code twice. The cost is that a change to it
rebuilds both services. If the services moved to separate repositories, it would become a versioned
NuGet package.

## Layers and dependency direction

```
        ┌────────────┐
        │    Api     │  Hot Chocolate schema, resolvers, auth policies, Program.cs (composition root)
        └──┬──────┬──┘
           │      │ (registers implementations)
           ▼      ▼
 ┌─────────────┐ ┌────────────────┐
 │ Application │◀│ Infrastructure │  EF Core, HTTP clients, providers — implements Application interfaces
 └──────┬──────┘ └────────────────┘
        ▼
   ┌─────────┐
   │ Domain  │  Entities, business rules, error codes. No dependencies.
   └─────────┘
```

- **Domain** references nothing. `Estimate`, `LineItem`, `PricingCalculator`, `EstimateRules` and
  `EstimateExport` hold every business rule, so they can be unit tested without a database or server.
- **Application** references Domain. It coordinates use cases (`EstimateService`,
  `EstimateExportService`) and declares the interfaces it needs, such as `IEstimateRepository` or
  `IAccountingProvider`. It doesn't know about EF Core, HTTP or GraphQL. It does reference
  `GreenDonut.Data.Primitives`, a tiny package that only contains the `PagingArguments` and `Page<T>`
  types, so paging can be pushed down to SQL without a hand-written paging abstraction.
- **Infrastructure** references Application and implements its interfaces. This is dependency
  inversion: the use cases define what they need, and the database or HTTP details plug in from outside.
- **Api** references both. It is the composition root that wires services together, and the only
  place that knows about GraphQL.

### Why these interfaces exist, and why there aren't more

Each interface has a concrete reason: it crosses a process or storage boundary, and there is a fake
for it in the tests.

| Interface | Reason |
|---|---|
| `IEstimateRepository`, `IEstimateExportRepository` | Write-side persistence for one aggregate, with only the operations the use cases need. Not a generic `IRepository<T>` |
| `IEstimateReadStore`, `IEstimateExportReadStore` | Read-side queries with a different lifetime and projection strategy (see below) |
| `IEstimatesServiceClient` | Remote call to another service |
| `IAccountingProvider` | Plug-in point for Mock, Xero and QuickBooks |

Application services (`EstimateService`) and the token provider are concrete classes: they have one
implementation, and tests construct them directly. There is no mediator library; resolvers call
application services directly.

## Request flow (export)

```
Client ──POST /graphql──▶ Accounting.Api
  1. JwtBearer validates the token (signature, iss, aud, exp)      → HTTP 401 if invalid
  2. [Authorize(Policy = accounting.export)] checks scope + tenant  → AUTH_NOT_AUTHORIZED if missing
  3. Resolver reads tenant_id from the token, calls EstimateExportService
  4. Existing export for (tenant, estimate, provider)?
        SUCCEEDED → return it (idempotent, no further calls)
        PENDING (recent) → EXPORT_IN_PROGRESS
  5. EstimatesServiceClient ──client-credentials token──▶ Estimates.Api estimateForIntegration
        not found → ESTIMATE_NOT_FOUND; not APPROVED → ESTIMATE_NOT_APPROVED
  6. INSERT export (PENDING) — the unique index blocks concurrent duplicates
  7. IAccountingProvider.ExportInvoiceAsync(invoice, idempotencyKey = export.Id)
  8. UPDATE export → SUCCEEDED (externalReference) or FAILED (lastErrorCode)
```

### Limitations of the synchronous integration (milestone 1)

- **The client waits** for the Estimates service *and* the accounting provider. Real providers can
  take seconds and have rate limits.
- **No automatic retry.** A failed export stays `FAILED` until a client calls `exportEstimate` again.
- **Temporal coupling.** If the Estimates service is down, exports fail with `ESTIMATES_SERVICE_UNAVAILABLE`.
- **A crash between steps 6 and 8** leaves an export `PENDING`. After `PendingTimeoutSeconds` (120 s)
  it is treated as abandoned and can be retried. The provider idempotency key (the export ID) lets a
  real provider ignore the duplicate if the first attempt actually reached it.
- **The provider call is not cancelled** when the client disconnects (a deliberate choice). It is
  bounded by `ProviderTimeoutSeconds`, so the outcome is always recorded.

[docs/roadmap.md](roadmap.md#milestone-3--asynchronous-integration-events-outbox-retries) describes
the asynchronous design (events, transactional outbox, background retries) that removes these limits.

## Key design decisions

**Explicit GraphQL types.** Types are declared with `BindFieldsExplicitly()`, and
`UseXmlDocumentation = false`. Adding a C# property never silently changes the public contract, and
internal XML comments never leak into schema descriptions.

**Read models, not entities, in the schema.** Resolvers return `EstimateReadModel`, not the
`Estimate` aggregate. List queries project straight to read models in SQL. Totals are stored on the
estimate row, so a list shows totals without loading any line items.

**Avoiding N+1 queries.** `Estimate.lineItems` is resolved through `LineItemsByEstimateDataLoader`.
A page of 50 estimates that each ask for `lineItems` runs **2** SQL queries (the page, then one
`WHERE estimate_id = ANY(...)`) instead of 51. The DataLoader key includes the tenant, so the batch
query filters by tenant too. Single-estimate queries go through the same DataLoader, so there is only
one code path.

**DbContext lifetimes.** Hot Chocolate may resolve sibling query fields in parallel, and a DbContext
isn't thread-safe. The read stores therefore create a short-lived context per call from
`IDbContextFactory`. Mutations run one after another, so the write-side repositories use the
request-scoped context, which keeps the aggregate change-tracked from load to save.

**Optimistic concurrency.** `Estimate.Version` is an EF Core concurrency token, so every UPDATE
includes `WHERE version = @original`. Clients also send `expectedVersion`. That catches stale edits
early with a friendly error, while the database check catches true races
(`Database_enforces_optimistic_concurrency_even_if_application_check_is_bypassed`).

**Idempotent export.** A natural key (tenant, estimate, provider) is guarded by a unique index
(`ux_estimate_exports_tenant_estimate_provider`), instead of a client-supplied idempotency key.
Exporting an estimate is inherently a once-only action, so the key follows from the request. The
integration test fires 5 concurrent duplicate requests and asserts exactly one row.

**Errors as part of the schema.** Expected business failures are typed errors in mutation payloads
(Hot Chocolate mutation conventions, `[Error(typeof(...))]`), each with a stable `code`. Unexpected
exceptions become `INTERNAL_ERROR` with a `traceId`; details go only to logs.

**Consumer-driven contract.** The Accounting service's only query against Estimates lives in
`EstimateForExport.graphql`, embedded in its client. The Estimates test suite executes that same file
(`ConsumerContractTests`), so a breaking schema change fails the Estimates build.

## Observability

- **Structured logs.** JSON console logs outside development, with `CorrelationId`, `TraceId` and
  `SpanId` in each entry's scope.
- **Correlation ID.** `X-Correlation-ID` is accepted if it is well-formed, otherwise the trace ID is
  used. It is returned to the caller and forwarded on the Accounting → Estimates call.
- **OpenTelemetry.** Traces cover ASP.NET Core, HttpClient, Hot Chocolate and Npgsql; metrics cover
  ASP.NET Core, HttpClient and `graphql.errors`; logs are included. W3C `traceparent` links a
  request's spans across both services. Data is exported over OTLP only when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set. In Docker mode that is the Aspire dashboard at
  http://localhost:18888.
- **GraphQL errors behind HTTP 200.** `GraphQLErrorLoggingListener` logs every request, validation
  and resolver error, and counts them in the `graphql.errors` metric by kind and code. Error rates are
  therefore visible even though GraphQL mostly answers with 200.
- **Health.** `/health/live` checks only that the process runs. `/health/ready` checks the service's
  own database. The Accounting service stays ready while Estimates is down, because it can still
  answer status queries.
