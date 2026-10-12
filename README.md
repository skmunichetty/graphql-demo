# Construction Estimation — GraphQL microservices (Milestone 1)

A learning project: two independently deployable ASP.NET Core services with Hot Chocolate GraphQL
APIs, secured with OAuth 2.0 / OpenID Connect access tokens.

| Service | Owns | GraphQL endpoint (local) |
|---|---|---|
| **Estimates** | Estimates, line items, pricing, estimate status | `http://localhost:5101/graphql` |
| **Accounting Integration** | Accounting-provider integration and export records | `http://localhost:5102/graphql` |

> **Status: a working, tested local milestone — not production-ready.** It runs locally with a
> demo identity provider and a **mock** accounting provider. See
> [docs/deployment.md](docs/deployment.md#remaining-production-work) for the work still needed
> before production.

## What milestone 1 does

- Create a draft estimate (customer details and one currency), then add, update and remove line items.
- Get an estimate by ID; list estimates with bounded cursor pagination.
- Approve an estimate; approved estimates can't be changed.
- Export an approved estimate to a **mock** accounting provider. The export is idempotent: repeating it never creates a duplicate.
- Query export status.
- JWT authentication, scope-based authorization and tenant isolation in data access.
- Service-to-service calls use a separate, least-privilege service identity.

## Architecture in one picture

```
             access token (user: tenant_id + scopes)                 ┌──────────────┐
  Client ───────────────────────────────────────────────────────────▶│  Keycloak    │ (local OIDC
    │                                                                 │  (or Auth0)  │  provider)
    │ POST /graphql  Authorization: Bearer …                          └──────▲───────┘
    ▼                                                                        │ client credentials
┌─────────────────────┐   GraphQL: estimateForIntegration          ┌─────────┴───────────┐
│ Estimates service   │◀───────────────────────────────────────────│ Accounting service  │
│ Api → Application   │   Bearer <service token,                    │ Api → Application   │
│      → Domain       │   scope estimates:integration.read>         │      → Domain       │
│ Infrastructure(EF)  │                                             │ Infrastructure(EF,  │
└─────────┬───────────┘                                             │  HTTP, providers)   │
          │                                                         └─────────┬───────────┘
   ┌──────▼──────┐   separate databases and login roles               ┌──────▼──────┐
   │ estimates DB│   (same PostgreSQL container locally)              │ accounting DB│──▶ Mock provider
   └─────────────┘                                                    └─────────────┘
```

Read [docs/architecture.md](docs/architecture.md) for the layers, dependency direction and design decisions.

## Versions (checked 2026-09-28)

| Component | Version | Notes |
|---|---|---|
| .NET SDK / ASP.NET Core / EF Core | 10.0 (LTS, supported to Nov 2028) | `global.json` pins SDK 10.0.1xx or later |
| Hot Chocolate | 16.6.7 | GraphQL server, authorization, DataLoader, cost analysis |
| Npgsql EF Core provider | 10.0.3 | PostgreSQL |
| PostgreSQL | 17 (alpine image) | |
| Keycloak | 26.7.4 | Local OIDC identity provider |
| xUnit v3 + Microsoft.Testing.Platform | 4.0.1 | Testcontainers 4.15.0 for PostgreSQL |
| OpenTelemetry .NET | 1.19 | OTLP export to the Aspire dashboard locally |

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (`dotnet --version` should print 10.0.1xx or later)
- Docker Desktop, or another Docker engine with Compose v2
- PowerShell (Windows) or bash with `openssl` and `curl` (macOS/Linux)

Host ports used: **5101** (Estimates), **5102** (Accounting), **8180** (Keycloak), **55432**
(PostgreSQL) and **18888** (Aspire dashboard). The less common Keycloak and PostgreSQL ports avoid
clashes with other local projects. To change them, set `KEYCLOAK_HOST_PORT` / `POSTGRES_HOST_PORT`
in `.env`, then update the matching URLs in `appsettings.Development.json` and `scripts/`.

## Quick start

### 1. Generate local secrets (once)

Creates a gitignored `.env` with random passwords and the service client secret. It also stores the
connection strings as [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
for `dotnet run`. Nothing secret is committed.

```powershell
./scripts/init-dev-env.ps1
```

```bash
./scripts/init-dev-env.sh
```

### 2a. Run everything in Docker

```bash
docker compose up -d --build
```

### 2b. Or run the infrastructure in Docker and the APIs from source (best for learning and debugging)

```bash
docker compose up -d postgres keycloak
```

```bash
dotnet run --project src/Estimates/Estimates.Api
```

```bash
dotnet run --project src/Accounting/Accounting.Api
```

Start the two `dotnet run` commands in separate terminals, one after the other. Database migrations
run automatically in the Development environment.

### 3. Check it works

```bash
curl http://localhost:5101/health/ready
```

```powershell
./scripts/e2e-walkthrough.ps1
```

The walkthrough authenticates, creates an estimate, adds line items, approves it, exports it through
the mock provider, and queries the export status. Along the way it checks the negative cases:
401, validation, concurrency, tenant isolation, draft export and edits after approval.

## Local URLs

| What | URL | Notes |
|---|---|---|
| Estimates GraphQL | `http://localhost:5101/graphql` | POST only, bearer token required |
| Accounting GraphQL | `http://localhost:5102/graphql` | POST only, bearer token required |
| Nitro GraphQL IDE | `http://localhost:5101/graphql/ui`, `http://localhost:5102/graphql/ui` | Development only; add an `Authorization` header in the IDE |
| Schema (SDL) | `/graphql/schema.graphql` on each service | Development only; checked-in copies in [`schema/`](schema) |
| Health | `/health/live`, `/health/ready` | Anonymous |
| Keycloak | `http://localhost:8180` (admin console user `admin`, password in `.env`) | Realm `construction` |
| Aspire dashboard (traces, logs, metrics) | `http://localhost:18888` | Docker mode only |

## Getting a development token

```powershell
$token = ./scripts/get-token.ps1 -User alice
```

```bash
TOKEN=$(./scripts/get-token.sh alice)
```

Demo users: **alice** (tenant `tenant-acme`) and **bob** (tenant `tenant-bayside`). Their password
is `DEMO_USER_PASSWORD` in `.env`. To request fewer scopes, for example to see an authorization
failure: `./scripts/get-token.ps1 -User alice -Scopes 'estimates:read'`.

The [client integration guide](docs/client-integration-guide.md) has copyable operations, curl
examples, real responses, error handling and pagination.

## Running the tests

Integration tests need Docker running; Testcontainers starts a throwaway PostgreSQL for them.

```bash
dotnet test --solution ConstructionEstimation.slnx
```

| Project | What it covers |
|---|---|
| `Estimates.UnitTests` | Totals, tax, rounding, invalid quantities/prices/currency, the approval lock, concurrency and tenant checks in the application layer |
| `Estimates.IntegrationTests` | Real GraphQL endpoint and PostgreSQL: lifecycle, 401 (no token, wrong audience, expired, forged signature, wrong issuer), missing scopes, cross-tenant access, pagination and limits, introspection off, database-level optimistic concurrency, schema snapshot, and the Accounting consumer contract |
| `Accounting.UnitTests` | Export idempotency and retry state machine, provider failures and timeouts, the mock provider, the Estimates client (including GraphQL errors sent with HTTP 200), token caching |
| `Accounting.IntegrationTests` | Real endpoint and PostgreSQL: export, idempotent repeats, 5 concurrent duplicates → 1 row, unapproved/unknown estimate, Estimates outage, mock provider failure then retry, tenant isolation, auth failures, schema snapshot |

If you change the schema on purpose, update the snapshots with
`UPDATE_SCHEMA_SNAPSHOTS=1 dotnet test --solution ConstructionEstimation.slnx`, then review the diff
in `schema/*.graphql` (see [docs/schema-evolution.md](docs/schema-evolution.md)).

## Repository layout

```
src/
  Estimates/        Estimates.Domain · .Application · .Infrastructure · .Api (+ Dockerfile)
  Accounting/       Accounting.Domain · .Application · .Infrastructure · .Api (+ Dockerfile)
  ServiceDefaults/  Technical cross-cutting code only (auth plumbing, telemetry, GraphQL limits)
tests/              Unit and integration tests; TestSupport = test-only JWT signer and helpers
schema/             Checked-in SDL for each service (reviewed on every change)
deploy/             Keycloak realm import, PostgreSQL init script
scripts/            Secret generation, token helper, end-to-end walkthrough
docs/               Architecture, client guide, security, pricing, schema evolution, deployment, roadmap
```

## Demo configuration vs production configuration

| Area | Demo (this repository) | Production |
|---|---|---|
| Identity provider | Keycloak `start-dev`, HTTP, imported realm, password grant enabled for the CLI client | Managed OIDC provider (Auth0, Entra ID, hardened Keycloak) over HTTPS; authorization code + PKCE for users, client credentials for services; no password grant |
| Secrets | Random values in gitignored `.env` and .NET user secrets | Secret manager (Azure Key Vault, AWS Secrets Manager, Vault) injected as environment variables |
| GraphQL tooling | Nitro IDE, SDL download, introspection, exception details | All off (the defaults in `appsettings.json`) |
| HTTPS | Plain HTTP on localhost | TLS at the ingress, `EnforceHttps=true` (HSTS and redirect), `RequireHttpsMetadata=true` |
| Migrations | Applied at startup | A separate deployment step |
| Accounting provider | Clearly labelled **mock**, in memory | Real providers (Xero, QuickBooks) — a later milestone |
| Databases | Two databases in one PostgreSQL container | Separate managed databases or servers per service, backups, least-privilege roles |

## Documentation

- [Architecture and design decisions](docs/architecture.md)
- [Client integration guide](docs/client-integration-guide.md)
- [Security: authentication, authorization, tenants, service identity](docs/security.md)
- [Pricing, tax and rounding rules](docs/pricing-rules.md)
- [Schema evolution and deprecation](docs/schema-evolution.md)
- [Deployment guidance and remaining production work](docs/deployment.md)
- [Roadmap: later milestones](docs/roadmap.md)
