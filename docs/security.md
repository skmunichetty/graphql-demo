# Security

## Authentication

- The APIs are **resource servers only**. They validate access tokens issued by an external
  OpenID Connect provider. They have no login endpoint, no password storage and no token issuer.
- `ServiceDefaults/Security/JwtAuthenticationSetup.cs` validates:
  - the **signature**, with keys from the provider's discovery document (`/.well-known/openid-configuration`),
    which are refreshed automatically when keys rotate;
  - the **issuer** (`iss` must equal `Authentication:Authority` exactly);
  - the **audience** (`aud` must contain `estimates-api` or `accounting-api`, so a token for one API
    is rejected by the other — see the test `Estimates_token_cannot_call_accounting_api`);
  - the **lifetime** (`exp` is required, with 30 s of clock skew);
  - the **algorithm** (RS256 only; unsigned tokens and symmetric algorithms are rejected).
- `/graphql` requires an authenticated user at the HTTP level (`RequireAuthorization()`), so a missing
  or invalid token gets a plain **401** before any GraphQL runs.
- Authentication failures are logged by *type* (expired, bad signature…). Tokens are never logged.

## Authorization

| Policy | Requires |
|---|---|
| `estimates.read` / `estimates.write` | Authenticated, a `tenant_id` claim, and the scope `estimates:read` / `estimates:write` |
| `estimates.integration.read` | Authenticated and the scope `estimates:integration.read` (service identities) |
| `accounting.export` / `accounting.read` | Authenticated, a `tenant_id` claim, and the scope `accounting:export` / `accounting:read` |

Policies are attached to each field with Hot Chocolate's `[Authorize(Policy = …)]` (note the
`HotChocolate.Authorization` namespace; the ASP.NET Core attribute would be ignored by GraphQL).
A missing scope produces `AUTH_NOT_AUTHORIZED` for that field only. The scope check reads `scope`,
`scp` (Entra ID) and `permissions` (Auth0 RBAC).

## Tenant isolation

1. **Tenant comes from the token.** Resolvers call `TenantClaimReader.GetRequiredTenantId(user)`.
   No user-facing operation accepts a tenant argument.
2. **Enforced in data access.** Every repository and read-store query includes
   `WHERE tenant_id = @tenant`, including the DataLoader batch query. Application services receive
   the tenant as an explicit parameter.
3. **Indistinguishable from "not found".** Another tenant's estimate returns `null` or
   `ESTIMATE_NOT_FOUND`, never "forbidden", so tenants can't probe each other's IDs.
4. **Tested.** Unit tests cover the application layer. Integration tests check reads, lists,
   mutations and exports across two tenants against real PostgreSQL. The end-to-end script checks it
   with real Keycloak users.
5. **Tenant can't be self-assigned.** In the Keycloak realm, `tenant_id` is a user-profile attribute
   that only administrators can view or edit, so users can't change it in the account console.

## Service-to-service calls

The Accounting service calls Estimates as **itself**, using the client credentials grant with client
`accounting-service`:

- **Least privilege.** Its only scope is `estimates:integration.read`. Keycloak refuses to issue it
  anything else, and the Estimates API lets that scope call only `estimateForIntegration`.
- **Credentials stay server-side.** The client secret is supplied through configuration (user
  secrets locally, a secret store in production) and is never logged.
- **Token caching.** Tokens are cached until 60 s before expiry, with a single refresh at a time.

**Trade-off: passing the tenant.** A service token represents the Accounting service, not a user, so
it has no `tenant_id`. The Accounting service therefore passes the tenant as an argument to
`estimateForIntegration`. That tenant always comes from the end user's *validated* token in the
Accounting service, and the Estimates service only accepts a tenant argument from callers holding the
integration scope. User tokens can't use that query (`User_token_cannot_use_integration_query_to_pick_a_tenant`).
The risk that remains: a compromised Accounting service could read *any* tenant's estimates.

**Stronger alternative (later).** [OAuth 2.0 Token Exchange (RFC 8693)](https://www.rfc-editor.org/rfc/rfc8693):
the Accounting service exchanges the user's token for a downstream token that carries the user's
tenant, the Accounting service as `act` (actor) and the narrow scope. The Estimates service then
derives the tenant from claims again. Keycloak supports token exchange; so do Auth0 (via Custom Token
Exchange) and Entra ID (via the on-behalf-of flow).

## Using Auth0 (or another OIDC provider) in production

Nothing in the code is specific to Keycloak. To switch to Auth0:

1. Create two **APIs** in Auth0 with identifiers `estimates-api` and `accounting-api`, signing
   algorithm RS256. Add permissions (scopes): `estimates:read`, `estimates:write`,
   `estimates:integration.read`, `accounting:export`, `accounting:read`.
2. Create a **Machine-to-Machine application** for the Accounting service, authorized for
   `estimates-api` with only `estimates:integration.read`.
3. Add the tenant claim with a **post-login Action**, for example
   `api.accessToken.setCustomClaim('tenant_id', event.user.app_metadata.tenant_id)`. Keep it in
   `app_metadata`, which users can't edit. Auth0 lets you use either a namespaced claim such as
   `https://example.com/tenant_id` or a plain `tenant_id`; set `Authentication:TenantClaimType` to match.
4. Configure each API:

   ```
   Authentication__Authority=https://YOUR_DOMAIN.auth0.com/        # trailing slash: must equal "iss"
   Authentication__Audience=estimates-api
   Authentication__RequireHttpsMetadata=true
   Authentication__TenantClaimType=tenant_id
   ```

   and for the Accounting service:

   ```
   EstimatesService__TokenEndpoint=https://YOUR_DOMAIN.auth0.com/oauth/token
   EstimatesService__ClientId=<M2M client id>
   EstimatesService__ClientSecret=<from secret store>
   ```

   Auth0's client credentials grant uses an `audience` parameter instead of `scope`. The token
   provider sends `scope`; for Auth0, add `audience=estimates-api` to the form in
   `ClientCredentialsTokenProvider` (a small, planned change), or use a provider-neutral token client
   library.
5. Use **authorization code + PKCE** for user-facing apps. Don't enable the password grant.

## Secrets

- Nothing secret is committed. `.env` is gitignored; `.env.example` has empty placeholders.
- Local values are random and generated per machine (`scripts/init-dev-env.*`).
- The Keycloak realm file uses `${ENV}` placeholders for passwords and the client secret.
- Test tokens are signed with a key generated in memory for each test run (`tests/TestSupport`).
- In production, inject secrets from a secret manager as environment variables
  (`ConnectionStrings__EstimatesDb`, `EstimatesService__ClientSecret`) or through a configuration
  provider such as Azure Key Vault.
- **Future accounting-provider OAuth credentials** (Xero or QuickBooks client secrets and refresh
  tokens) will be stored server-side only, encrypted at rest, per tenant, and never returned by any
  API. See [roadmap.md](roadmap.md#milestone-4--real-accounting-providers).

## Other controls

- CORS uses an explicit origin allow-list (empty by default), POST only, a fixed set of allowed
  headers, and no credentials.
- GraphQL GET requests are disabled, which blocks CSRF-style triggering and keeps queries out of URL logs.
- Request body size, query depth, operation cost, page size, execution timeout and a per-client rate
  limit are all bounded (see the client guide).
- Introspection, the IDE and exception details are off by default and enabled only by the
  Development configuration.
- Containers run as the non-root `app` user.
