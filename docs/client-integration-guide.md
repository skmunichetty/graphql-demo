# Client integration guide

This guide is for developers of applications that call the Estimates and Accounting APIs.
Everything below was run against the local stack. Response bodies are real, except that IDs and
references have been shortened.

- [1. Endpoints](#1-endpoints)
- [2. Authentication](#2-authentication)
- [3. Estimates API operations](#3-estimates-api-operations)
- [4. Accounting API operations](#4-accounting-api-operations)
- [5. Errors: three kinds, handled differently](#5-errors-three-kinds-handled-differently)
- [6. Pagination](#6-pagination)
- [7. Concurrency (`expectedVersion`)](#7-concurrency-expectedversion)
- [8. Limits](#8-limits)
- [9. Service-to-service authentication](#9-service-to-service-authentication)
- [10. Schema documentation and deprecation](#10-schema-documentation-and-deprecation)

## 1. Endpoints

| API | Local URL | Audience (`aud`) |
|---|---|---|
| Estimates | `POST http://localhost:5101/graphql` | `estimates-api` |
| Accounting | `POST http://localhost:5102/graphql` | `accounting-api` |

- Send `POST` requests with `Content-Type: application/json` and a body of
  `{"query": "...", "variables": {...}, "operationName": "..."}`. `GET` is disabled.
- Send `Accept: application/graphql-response+json, application/json`. Responses use the
  [GraphQL over HTTP](https://graphql.github.io/graphql-over-http/) media type
  `application/graphql-response+json`.
- Optional: send `X-Correlation-ID` (1–64 characters from `A-Z a-z 0-9 . _ -`). It comes back in the
  response header and appears in every server log line for your request. Quote it when reporting a problem.

## 2. Authentication

Every request to `/graphql` needs an OAuth 2.0 access token (a JWT) from the configured OpenID Connect
provider:

```
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6...
```

The APIs check the token's signature, issuer (`iss`), audience (`aud`), expiry and algorithm (RS256).
Your **tenant** comes from the token's `tenant_id` claim; no API accepts a tenant from request input.

### Scopes

| Scope | API | Allows |
|---|---|---|
| `estimates:read` | Estimates | `estimate`, `estimates` |
| `estimates:write` | Estimates | `createEstimate`, `addLineItem`, `updateLineItem`, `removeLineItem`, `approveEstimate` |
| `accounting:export` | Accounting | `exportEstimate` |
| `accounting:read` | Accounting | `estimateExport`, `estimateExportById`, `estimateExports` |
| `estimates:integration.read` | Estimates | `estimateForIntegration` — **service identities only**, see [section 9](#9-service-to-service-authentication) |

Request only the scopes your application needs.

### Getting a development token (LOCAL DEMO ONLY)

The local Keycloak realm has a public client, `construction-dev-cli`, with the password grant enabled
so you can get a token from a terminal. **This is for local development only.** Real user-facing
applications use the authorization code flow with PKCE, and back-end services use client credentials.

PowerShell:

```powershell
$token = ./scripts/get-token.ps1 -User alice -Scopes 'estimates:read estimates:write accounting:export accounting:read'
```

bash:

```bash
TOKEN=$(./scripts/get-token.sh alice "estimates:read estimates:write accounting:export accounting:read")
```

The raw request the scripts make:

```bash
curl -s http://localhost:8180/realms/construction/protocol/openid-connect/token \
  -d grant_type=password -d client_id=construction-dev-cli \
  -d username=alice --data-urlencode "password=$DEMO_USER_PASSWORD" \
  --data-urlencode "scope=openid estimates:read estimates:write"
```

A decoded token looks like this:

```json
{
  "iss": "http://localhost:8180/realms/construction",
  "aud": ["estimates-api", "accounting-api"],
  "azp": "construction-dev-cli",
  "scope": "openid estimates:read estimates:write accounting:read accounting:export",
  "tenant_id": "tenant-acme",
  "exp": 1790635249
}
```

Tokens expire after 5 minutes. When you get a 401, get a new token and retry.

## 3. Estimates API operations

The curl examples use bash. In Windows PowerShell, call `curl.exe` (not the `curl` alias) and put
the JSON body in a file (`--data @body.json`), or use `Invoke-RestMethod` as in
`scripts/e2e-walkthrough.ps1`.

### Create a draft estimate

```graphql
mutation CreateEstimate($input: CreateEstimateInput!) {
  createEstimate(input: $input) {
    estimate { id status version currencyCode total customer { name email } }
    errors { __typename ... on UserError { code message } ... on ValidationError { field } }
  }
}
```

```json
{ "input": { "customerName": "Harbour View Apartments", "customerEmail": "pm@harbourview.example", "currencyCode": "AUD" } }
```

```bash
curl -s http://localhost:5101/graphql \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"query":"mutation($input: CreateEstimateInput!){ createEstimate(input:$input){ estimate { id status version } errors { ... on UserError { code message } } } }","variables":{"input":{"customerName":"Harbour View Apartments","currencyCode":"AUD"}}}'
```

```json
{ "data": { "createEstimate": { "estimate": { "id": "01a0ea2c-d858-7c53-…", "status": "DRAFT", "version": 1 }, "errors": null } } }
```

Supported currencies: `AUD`, `CAD`, `EUR`, `GBP`, `NZD`, `USD`. Each estimate has exactly one currency.

### Add a line item

```graphql
mutation AddLineItem($input: AddLineItemInput!) {
  addLineItem(input: $input) {
    estimate { id version subtotal taxTotal total lineItems { id position description quantity unitPrice taxRatePercent subtotal taxAmount total } }
    errors { __typename ... on UserError { code message } ... on ValidationError { field } }
  }
}
```

```json
{ "input": { "estimateId": "01a0ea2c-d858-7c53-…", "expectedVersion": 1,
             "description": "Structural steel beams", "quantity": 12, "unitPrice": 845.50, "taxRatePercent": 10 } }
```

`taxRatePercent` is optional; when you leave it out, the server's configured demo default (10) is
used. The response contains the whole updated estimate, including the new `version`. Use that
version for your next change.

### Update and remove line items

```graphql
mutation UpdateLineItem($input: UpdateLineItemInput!) {
  updateLineItem(input: $input) { estimate { id version total } errors { __typename ... on UserError { code message } } }
}

mutation RemoveLineItem($input: RemoveLineItemInput!) {
  removeLineItem(input: $input) { estimate { id version total } errors { __typename ... on UserError { code message } } }
}
```

```json
{ "input": { "estimateId": "…", "lineItemId": "…", "expectedVersion": 3,
             "description": "Structural steel beams (galvanised)", "quantity": 12, "unitPrice": 910, "taxRatePercent": 10 } }
```

```json
{ "input": { "estimateId": "…", "lineItemId": "…", "expectedVersion": 4 } }
```

### Approve

```graphql
mutation ApproveEstimate($input: ApproveEstimateInput!) {
  approveEstimate(input: $input) { estimate { id status version approvedAt } errors { __typename ... on UserError { code message } } }
}
```

An estimate needs at least one line item before it can be approved. After approval, every change
returns `ESTIMATE_NOT_EDITABLE`.

### Read one estimate, or list them

```graphql
query GetEstimate($id: UUID!) {
  estimate(id: $id) { id status currencyCode subtotal taxTotal total version customer { name } lineItems { description total } }
}

query ListEstimates($first: Int, $after: String) {
  estimates(first: $first, after: $after) {
    nodes { id status total currencyCode customer { name } }
    pageInfo { hasNextPage endCursor }
  }
}
```

`estimate` returns `null` both when the ID doesn't exist and when it belongs to another tenant, so
callers can't learn which IDs exist elsewhere.

## 4. Accounting API operations

### Export an approved estimate

```graphql
mutation ExportEstimate($input: ExportEstimateInput!) {
  exportEstimate(input: $input) {
    export { id estimateId provider status externalReference exportedTotal currencyCode attemptCount lastErrorCode lastErrorMessage }
    errors { __typename ... on UserError { code message } }
  }
}
```

```json
{ "input": { "estimateId": "01a0ea2c-d858-7c53-…" } }
```

`provider` is optional and defaults to `MOCK`, the only provider in milestone 1.

```bash
curl -s http://localhost:5102/graphql \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"query":"mutation($input: ExportEstimateInput!){ exportEstimate(input:$input){ export { id status externalReference } errors { ... on UserError { code message } } } }","variables":{"input":{"estimateId":"01a0ea2c-d858-7c53-…"}}}'
```

Success:

```json
{ "data": { "exportEstimate": { "export": { "id": "01a0ea2c-df7f-…", "status": "SUCCEEDED",
  "externalReference": "MOCK-INV-343850A5D959", "exportedTotal": 12535.60, "currencyCode": "AUD", "attemptCount": 1 }, "errors": null } } }
```

**Idempotency.** There is at most one export per estimate and provider. Sending the same mutation
again returns the **same** export (same `id` and `externalReference`) without contacting the
provider again. This makes retries after a timeout or network error safe; you don't need an
idempotency key.

**Provider failure.** When the accounting provider rejects or fails the request, the export is still
recorded and returned with `status: FAILED`, and `errors` is `null`. **Always check `status`.**
Calling `exportEstimate` again retries the same export record, and `attemptCount` goes up. With the
mock provider, a customer name containing `MOCK-FAIL` simulates a rejection:

```json
{ "data": { "exportEstimate": { "export": { "status": "FAILED", "attemptCount": 1,
  "lastErrorCode": "ACCOUNTING_PROVIDER_REJECTED",
  "lastErrorMessage": "Mock provider rejected the invoice because the customer name contains 'MOCK-FAIL'.",
  "externalReference": null }, "errors": null } } }
```

Export status values: `PENDING` (in progress), `SUCCEEDED` (final), `FAILED` (can be retried).

### Query export status

```graphql
query ExportStatus($estimateId: UUID!) {
  estimateExport(estimateId: $estimateId) { id status externalReference attemptCount lastErrorCode completedAt }
}

query ExportById($id: UUID!) { estimateExportById(id: $id) { id status } }

query Exports($first: Int, $after: String) {
  estimateExports(first: $first, after: $after) { nodes { id estimateId status } pageInfo { hasNextPage endCursor } }
}
```

`estimateExport` returns `null` if the estimate has never been exported in your tenant.

## 5. Errors: three kinds, handled differently

GraphQL can report failures in three places. A robust client checks all three.

| Kind | Where it appears | HTTP status | Example | What the client should do |
|---|---|---|---|---|
| **Transport / request error** | HTTP status; body may be empty or have top-level `errors` and no `data` | 401, 400, 413, 429, 5xx | Missing or expired token → **401**; malformed query or bad variables → **400**; rate limit → **429** | Fix the request, refresh the token, or back off and retry (429/5xx) |
| **GraphQL execution error** | Top-level `errors` array, with `extensions.code`; affected fields are `null` | Usually **200** | `AUTH_NOT_AUTHORIZED`, `HC0051` (page too large), `INTERNAL_ERROR` | Don't assume HTTP 200 means success. Check `errors` in every response |
| **Business / validation error** | The mutation payload's `errors` list, as typed objects | **200** | `VALIDATION_FAILED`, `ESTIMATE_NOT_EDITABLE`, `CONCURRENCY_CONFLICT` | Show the message or act on the `code`; these are expected outcomes |

Real examples:

No token (transport):

```
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer
```

Unknown field (validation of the GraphQL document, transport level):

```
HTTP/1.1 400 Bad Request
{"errors":[{"message":"The field `secret` does not exist on the type `Estimate`.","locations":[{"line":1,"column":23}], …}]}
```

Missing scope (execution error, HTTP 200):

```json
{"errors":[{"message":"The current user is not authorized to access this resource.","path":["createEstimate"],
  "extensions":{"code":"AUTH_NOT_AUTHORIZED"}}],"data":null}
```

Business error (inside the payload, HTTP 200):

```json
{"data":{"addLineItem":{"estimate":null,"errors":[{"__typename":"ValidationError","code":"VALIDATION_FAILED",
  "message":"Quantity must be greater than zero.","field":"quantity"}]}}}
```

Unexpected server failure (execution error). The details are only in the server logs; use `traceId`
when you contact support:

```json
{"errors":[{"message":"An unexpected error occurred. Quote the traceId if you contact support.","path":["…"],
  "extensions":{"code":"INTERNAL_ERROR","traceId":"a318b3c930b0e0b9ce8701572eee96d5"}}]}
```

### Business error codes (stable — never renamed or reused)

| Code | Type | Returned by | Meaning |
|---|---|---|---|
| `VALIDATION_FAILED` | `ValidationError` (+ `field`) | Estimates mutations | Input broke a rule, for example quantity ≤ 0 |
| `ESTIMATE_NOT_FOUND` | `EstimateNotFoundError` | Estimates mutations, `exportEstimate` | Not found **in your tenant** |
| `LINE_ITEM_NOT_FOUND` | `LineItemNotFoundError` | `updateLineItem`, `removeLineItem` | The line item isn't on this estimate |
| `ESTIMATE_NOT_EDITABLE` | `InvalidEstimateStateError` | Estimates mutations | The estimate is approved (locked) |
| `ESTIMATE_HAS_NO_LINE_ITEMS` | `InvalidEstimateStateError` | `approveEstimate` | Add a line item first |
| `CONCURRENCY_CONFLICT` | `ConcurrencyConflictError` | Estimates mutations | Somebody else changed it; see [section 7](#7-concurrency-expectedversion) |
| `ESTIMATE_NOT_APPROVED` | `EstimateNotApprovedError` | `exportEstimate` | Only approved estimates can be exported |
| `EXPORT_IN_PROGRESS` | `ExportInProgressError` | `exportEstimate` | Another request is exporting it; poll `estimateExport` |
| `ESTIMATES_SERVICE_UNAVAILABLE` | `EstimatesServiceUnavailableError` | `exportEstimate` | Dependency down; nothing was exported; retry later |
| `UNSUPPORTED_ACCOUNTING_PROVIDER` | `UnsupportedProviderError` | `exportEstimate` | Provider not configured on this server |

Stored on a failed export (`lastErrorCode`), not returned as errors: `ACCOUNTING_PROVIDER_REJECTED`,
`ACCOUNTING_PROVIDER_UNAVAILABLE`, `ACCOUNTING_PROVIDER_TIMEOUT`.

Common execution error codes from the server: `AUTH_NOT_AUTHORIZED`, `AUTH_NOT_AUTHENTICATED`,
`HC0051` (page size above the maximum), `HC0047` (operation cost too high), `INTERNAL_ERROR`.

**Rule of thumb.** Branch on `code` or `__typename`. Never parse `message`: messages are for people
and may change. Treat unknown codes and unknown enum values as a generic failure, because new ones
may be added.

## 6. Pagination

`estimates` and `estimateExports` use cursor ([Relay connection](https://relay.dev/graphql/connections.htm)) pagination:

- `first` (default **20**, maximum **50**) plus `after: <endCursor>` to go forward; `last` plus
  `before` to go backward.
- A `first` or `last` above 50 is rejected with `HC0051`. The server never returns an unbounded list.
- Results are newest first and stable. Cursors are keyset positions, not offsets, so new estimates
  created while you page don't make you skip or repeat items.
- Treat cursors as opaque strings. Don't build or store them long-term.
- There is deliberately no `totalCount`: counting a whole tenant's data on every page is expensive.

```graphql
query { estimates(first: 20) { nodes { id } pageInfo { hasNextPage endCursor } } }
query { estimates(first: 20, after: "<endCursor from previous page>") { nodes { id } pageInfo { hasNextPage endCursor } } }
```

Loop until `hasNextPage` is `false`.

## 7. Concurrency (`expectedVersion`)

Each estimate has a `version`. Every change to an estimate requires `expectedVersion`, the version
you last read. If someone else changed the estimate in the meantime, you get `CONCURRENCY_CONFLICT`
and nothing is changed. To recover: query the estimate again, re-apply your change to the fresh data
(or ask the user), and resend with the new `version`. The database checks the version as well, so
two requests racing each other can't both win.

## 8. Limits

| Limit | Value | Result when exceeded |
|---|---|---|
| Page size | 50 | `HC0051` |
| Query depth | 8 levels | Validation error (HTTP 400) |
| Operation cost | Field cost 1,000, type cost 5,000 | `HC0047` |
| Execution timeout | 10 s (Estimates), 30 s (Accounting) | Execution error |
| Request body | 256 KB (Estimates), 64 KB (Accounting) | HTTP 413 |
| Rate limit | 100 requests / 10 s (Estimates), 50 / 10 s (Accounting), per client application | HTTP 429 |
| Batching | Disabled | One operation per request |

Send the header `GraphQL-Cost: report` to see an operation's calculated cost in `extensions.operationCost`.

## 9. Service-to-service authentication

Back-end services don't use user tokens and don't use passwords. They authenticate as themselves
with the OAuth 2.0 **client credentials** grant:

```bash
curl -s http://localhost:8180/realms/construction/protocol/openid-connect/token \
  -d grant_type=client_credentials -d client_id=accounting-service \
  --data-urlencode "client_secret=$ACCOUNTING_SERVICE_CLIENT_SECRET" \
  -d scope=estimates:integration.read
```

The resulting token has `aud: estimates-api`, `scope: estimates:integration.read` and **no
`tenant_id`**. It can call only `estimateForIntegration(tenantId, id)`. It can't list, create or
change estimates, and the identity provider refuses to issue it any other scope. The Accounting
service caches the token until 60 seconds before it expires.

Because a service token isn't tied to one tenant, the calling service names the tenant it is acting
for. That tenant always comes from the *original user's* validated token in the Accounting service.
[docs/security.md](security.md#service-to-service-calls) explains this trade-off and the stronger
alternative (token exchange).

## 10. Schema documentation and deprecation

- The complete schemas are in [`schema/estimates.graphql`](../schema/estimates.graphql) and
  [`schema/accounting.graphql`](../schema/accounting.graphql). Every type and field has a description.
- In development you can explore them in the Nitro IDE (`/graphql/ui`) or download the SDL from
  `/graphql/schema.graphql`. Introspection is **disabled in production**; use the published schema
  files to generate clients.
- The schemas only grow in backward-compatible ways. Fields are deprecated with `@deprecated(reason: "…")`,
  kept for at least 90 days, and removed only after usage has stopped. Details and your
  responsibilities (handling unknown enum values, not relying on field order, and so on) are in
  [docs/schema-evolution.md](schema-evolution.md).
