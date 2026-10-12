# Schema evolution and deprecation

GraphQL APIs are usually not versioned by URL. A single schema evolves **additively** instead, and
old fields are retired gradually with deprecation. This works because every client asks only for the
fields it uses.

## How changes are controlled here

1. **Explicit types.** Object types use `BindFieldsExplicitly()`, so a field only appears in the
   schema when someone adds it on purpose in a `*Type` class.
2. **Checked-in schema.** `schema/estimates.graphql` and `schema/accounting.graphql` are snapshots.
   `SchemaSnapshotTests` fails whenever the running schema differs from the file, so every schema
   change shows up as a reviewable diff in the pull request.

   ```bash
   UPDATE_SCHEMA_SNAPSHOTS=1 dotnet test --solution ConstructionEstimation.slnx
   git diff schema/
   ```

3. **Consumer contracts.** Operations that other services depend on (for now
   `EstimateForExport.graphql`) are executed by the provider's tests.

## Safe (additive) changes

- Adding a type, a query or mutation field, or an optional argument or input field (nullable, or
  with a default).
- Adding a nullable output field.
- Adding an enum value. Clients are told to handle unknown values; see "Client rules" below.
- Adding a new error type to a mutation's error union.
- Making an argument *less* strict, for example non-null → nullable.

## Breaking changes (avoid, or do them through deprecation)

- Removing or renaming a field, argument, type or enum value.
- Changing a field's type, or making an output field nullable when it was non-null (clients may not
  handle `null`).
- Making an argument or input field required, or making an input stricter.
- Changing the meaning of an existing field or error `code`.

## Deprecation process

1. Add the replacement first, for example a new `customerReference` field.
2. Mark the old field `@deprecated` with a reason that names the replacement:

   ```csharp
   descriptor.Field(estimate => estimate.CurrencyCode)
       .Deprecated("Use `currency { code minorUnits }` instead. Will be removed after 2027-03-31.");
   ```

   which produces

   ```graphql
   currencyCode: String! @deprecated(reason: "Use `currency { code minorUnits }` instead. Will be removed after 2027-03-31.")
   ```

3. Announce the change and the removal date (at least **90 days** ahead) in the changelog and to
   registered consumers.
4. Watch usage. Hot Chocolate's OpenTelemetry instrumentation records field coordinates, so use of
   deprecated fields can be found in traces. A later milestone adds operation reporting.
5. Remove the field only after usage has stopped *and* the date has passed. Removal is a reviewed
   snapshot change.

## Client rules (published in the client guide)

- Request only the fields you use.
- Handle unknown enum values (`EstimateStatus`, `ExportStatus`, `AccountingProvider`) and unknown
  error `code` or `__typename` values without crashing.
- Don't depend on the order of fields in an object, or on error `message` text.
- Treat cursors as opaque.
- Watch for `@deprecated` in the published schema (most GraphQL tooling shows warnings).

## Later: composition

When more services join, the separate schemas can be composed behind a gateway
([roadmap](roadmap.md#milestone-2--schema-composition-and-federation)). The rules above still apply
to each subgraph.
