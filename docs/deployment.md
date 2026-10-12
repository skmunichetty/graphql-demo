# Deployment guidance

This milestone builds, passes its tests, and runs end to end locally. That does **not** make it
production-ready; the gaps are listed at the end of this document.

## Images

```bash
docker build -f src/Estimates/Estimates.Api/Dockerfile -t estimates-api:<version> .
docker build -f src/Accounting/Accounting.Api/Dockerfile -t accounting-api:<version> .
```

Each image contains one service (plus `ServiceDefaults`). Images are multi-stage builds, run as the
non-root `app` user, and listen on port 8080 over HTTP. TLS is terminated in front of the container.

## Production configuration

`appsettings.json` holds the safe production defaults: no IDE, no introspection, no exception
details, HTTPS enforced, and no migrations at startup. Required values have no default, so the
service **refuses to start** until they are supplied. The environment variables below override them
(`__` separates configuration sections).

| Setting | Estimates | Accounting | Source |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Production` | Deployment |
| `ConnectionStrings__EstimatesDb` / `__AccountingDb` | ✓ | ✓ | **Secret store** |
| `Authentication__Authority` | ✓ | ✓ | Deployment (the IdP issuer URL, HTTPS) |
| `Authentication__Audience` | `estimates-api` (default) | `accounting-api` (default) | Deployment |
| `EstimatesService__BaseAddress` | | ✓ (internal URL of Estimates) | Deployment |
| `EstimatesService__TokenEndpoint`, `__ClientId` | | ✓ | Deployment |
| `EstimatesService__ClientSecret` | | ✓ | **Secret store** |
| `Cors__AllowedOrigins__0` … | Browser app origins, if any | Same | Deployment |
| `OTEL_EXPORTER_OTLP_ENDPOINT` (+ `OTEL_EXPORTER_OTLP_HEADERS`) | Collector | Collector | Deployment / secret |

Settings are validated when the service starts: data-annotation rules on options classes, plus
`ValidateOnStart()` and `GetValidatedSettings()`. A missing connection string, an invalid authority
URL or an out-of-range timeout stops startup with a clear message instead of failing on the first request.

## HTTPS

- Terminate TLS at the load balancer or ingress, and keep `Hosting__EnforceHttps=true` (the default).
  That sends HSTS and redirects plain HTTP.
- Behind a proxy, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so ASP.NET Core trusts
  `X-Forwarded-Proto`. Restrict which proxies are trusted wherever your platform allows it.
- `Authentication__RequireHttpsMetadata=true` (the default) makes signing keys load over HTTPS only.
- Service-to-service traffic should also use TLS (a service mesh with mTLS, or HTTPS internal
  endpoints), even though the calls are already authenticated with tokens.

## Database migrations

Don't use `Hosting__ApplyMigrationsOnStartup=true` in production: several instances would race each
other, and the app would need schema-change permissions. Run migrations as a separate pipeline step
with an idempotent SQL script or a migration bundle:

```bash
dotnet ef migrations script --idempotent --project src/Estimates/Estimates.Infrastructure --startup-project src/Estimates/Estimates.Infrastructure -o estimates.sql
dotnet ef migrations bundle --project src/Accounting/Accounting.Infrastructure --startup-project src/Accounting/Accounting.Infrastructure -o efbundle-accounting
```

Run them with a migration role that has DDL rights. The application's own role then only needs data
(DML) rights.

## Health probes

- Liveness: `GET /health/live`, which only checks that the process runs.
- Readiness: `GET /health/ready`, which checks the service's own database.
- Health endpoints are anonymous and expose only `Healthy` / `Unhealthy`. Keep them off the public
  ingress if possible.

## Remaining production work

This list is intentionally frank. Items are grouped by risk.

**Must do before production**

- **Identity provider.** Use a hardened, managed OIDC tenant (Auth0 or another). Remove the demo
  password-grant client. Configure the authorization code + PKCE flow for user applications. Restrict
  who can receive `estimates:write` and `accounting:export` (roles/RBAC), not just "any user of the client".
- **Real secrets management and rotation.** This covers database passwords, the service client
  secret (prefer `private_key_jwt` or workload identity), and OTLP credentials.
- **TLS end to end** and network policy, so only the Accounting service can reach the Estimates
  service internally.
- **Managed PostgreSQL per service,** with backups, point-in-time recovery, TLS connections
  (`SSL Mode=VerifyFull`) and least-privilege roles (separate migration and runtime roles).
- **A real accounting provider** (the mock stores nothing durable), with per-tenant OAuth
  connections stored encrypted server-side.
- **Asynchronous export** with an outbox and retries (milestone 3), so provider outages don't fail
  user requests.
- **Data protection and privacy review.** Customer names and emails are personal data: define
  retention and deletion, and review logs for personal data.

**Should do**

- Token exchange (RFC 8693) instead of passing the tenant on the integration query (see security.md).
- Trusted documents / persisted operations for known clients, so the public API only runs
  pre-registered queries.
- Distributed rate limiting (the current limiter is per instance and in memory) and API gateway quotas.
- Dashboards and alerts on `graphql.errors`, latency, export failures and a growing number of stale
  `PENDING` exports.
- Image scanning, SBOMs, signed images, dependency update automation, and pinning base images by digest.
- A deployment pipeline: publish images, migrations, staged rollout, automated end-to-end smoke test
  (`scripts/e2e-walkthrough.ps1` is a starting point).
- Load and soak testing, including the cost limits, against realistic data volumes.
- Audit logging of approvals and exports (who, when, from which client).

**Known functional limitations of milestone 1**

- The estimate customer can't be edited after creation; there is no delete, archive or un-approve.
- There is no `totalCount` or filtering and sorting options on lists.
- The mock provider forgets its invoices on restart.
