# Roadmap: later milestones

None of these are implemented in milestone 1. Each builds on the foundations that are in place.

## Milestone 2 — Schema composition and federation

- **Goal:** one GraphQL endpoint for clients, while each service keeps owning its own schema.
- **Hot Chocolate Fusion:** each service becomes a *source schema*, and a Fusion gateway composes
  them. For example, `EstimateExport.estimate` resolves across services through a lookup field on
  the Estimates service.
- **Apollo Federation interoperability:** expose the subgraphs with Federation v2 directives
  (`@key`, `@shareable`), so an Apollo Router can compose them too. The schema snapshots and
  consumer contract tests become composition checks in CI.
- Prerequisites already in place: explicit types, stable IDs (UUIDs), and additive schema evolution.

## Milestone 3 — Asynchronous integration (events, outbox, retries)

The synchronous export is replaced with:

1. The Estimates service writes an `EstimateApproved` event to an **outbox table in the same
   transaction** as the approval. This avoids a "dual write": the database update and the message can
   never disagree.
2. A relay publishes outbox rows to a broker (RabbitMQ, Azure Service Bus or Kafka) at least once.
3. The Accounting service consumes the event, stores an `EstimateExport` in `PENDING` (the existing
   unique key makes duplicate deliveries harmless), and a background worker calls the provider.
4. **Retries** use exponential backoff and jitter, respect provider rate limits, and end in a
   dead-letter state with an alert. The existing idempotency key (the export ID) stops duplicate
   invoices when a retry follows an attempt that actually succeeded.
5. The `exportEstimate` mutation becomes "request export" and returns `PENDING` immediately; clients
   poll `estimateExport` or subscribe to status changes.
6. The event carries the estimate snapshot (or the consumer fetches it once), which removes the
   runtime dependency on the Estimates service being up.

## Milestone 4 — Real accounting providers

- `XeroAccountingProvider` and `QuickBooksAccountingProvider` implement `IAccountingProvider` and are
  registered in `AddAccountingInfrastructure`. The export flow doesn't change.
- A per-tenant **connection** flow: an OAuth 2.0 authorization code flow with the provider, started by
  a tenant admin. Refresh tokens are stored encrypted server-side (for example with ASP.NET Core Data
  Protection backed by a KMS key) and never returned to clients.
- Mapping to the provider's model: contacts, tax codes, account codes and currency checks.
  Provider-specific validation errors map to `ACCOUNTING_PROVIDER_REJECTED`, with details kept in the
  export record.
- Provider rate limits, webhooks for invoice status, and contract tests against provider sandboxes.

## Milestone 5 — React UI

- A React + TypeScript app using the authorization code flow with PKCE (the `construction-dev-cli`
  client already allows `http://localhost:5173`).
- A typed GraphQL client generated from `schema/*.graphql` (for example graphql-codegen with Relay or
  urql), fragments per component, cursor pagination, and handling of typed mutation errors.
- `Cors__AllowedOrigins` is already configurable.

## Milestone 6 — Kubernetes

- A Deployment per service, with the liveness and readiness probes already provided, resource
  requests and limits, a PodDisruptionBudget and a HorizontalPodAutoscaler.
- Secrets through the External Secrets Operator or the CSI secrets store; workload identity for cloud access.
- NetworkPolicies: only the Accounting pods may call the Estimates service internally.
- Migrations as a Kubernetes Job (or an Argo CD pre-sync hook) using EF migration bundles.

## Milestone 7 — Terraform

- Infrastructure as code for managed PostgreSQL per service, the container registry, the Kubernetes
  cluster, the secret store, the identity provider configuration (Auth0 has a Terraform provider) and
  observability back ends.
- Separate state per environment, and policy checks in CI.

## Milestone 8 — GitOps

- Argo CD or Flux syncs environment manifests (Helm or Kustomize) from Git.
- CI publishes signed images and opens pull requests that bump image tags. Promotion between
  environments happens through pull requests, with the end-to-end smoke test as a gate.
