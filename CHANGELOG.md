# Changelog

Notable changes to SnapData are documented here.

## 0.4.0 - 2026-10-27

- Use the SDK's built-in SourceLink and pin patched PKCS dependencies for the
  migration CLI and provider integration tests.
- Serialize shared-catalog provider test classes to avoid schema-inspection and
  DDL races; explicit concurrency contracts still execute parallel operations.

- Added `ForUpdate` on SQL, source, typed entity and projected queries for
  MySQL/PostgreSQL, with compiler capabilities and explicit rejection of
  unsupported providers and aggregate/count queries. Terminal query clones
  preserve row locks; callers execute through an explicit transaction.
- Added typed numeric `Increment` with mapped columns, parameterized amounts
  and existing key/generated-property and predicate guards.

- Hardened session/transaction cleanup after rollback or disposal failure;
  secondary errors remain inspectable, failed cleanup prevents session reuse,
  and internal batch/opening cleanup preserves original operation exceptions.
- Added isolated packed API consumers for .NET 8/10, including legacy custom
  compiler hooks and custom executor compatibility. New executor operations
  have explicit unsupported defaults rather than mandatory implementation.
- Fixed export delimiters after trailing SQL comments; exported files warn that
  raw execution does not update history or acquire the migration lock.
- Empty exports now produce an explicit no-op file, respecting overwrite
  protection and replacing stale SQL only with `--force`.
- Plain/no-color output preserves interactive confirmation; unavailable
  interaction reports a clear `--yes` requirement.
- Added mapped `InsertManyAsync` with bounded row/parameter budgets, explicit
  transaction ownership, successful-operation counts, and no input hydration.
  SQLite and four live provider contracts cover batching and rollback; Firebird
  uses single-row fallback and rejects unsupported standalone multi-row VALUES.
- Added explicit default-only SQL/typed inserts and generated-only entity
  inserts, verified against all five providers including SQLite.
- Added metadata-aware generated insert compilation; MySQL now rejects entity
  computed-column hydration instead of misusing LAST_INSERT_ID().
- Added typed `SetExpression` and aligned typed insert `Values` replacement
  behaviour with low-level insert builders.

- Added explicit-target single-row upsert SQL and entity builders for SQLite,
  PostgreSQL, and SQL Server, with update-selected-columns and do-nothing modes.
- Added live PostgreSQL and SQL Server upsert/returning contracts, including
  verified lock contention, rollback, and SQL Server blocked cancellation.
- Added compiler capability flags and provider-bound `Compile`/`Build` on
  session and transaction APIs without opening database connections.
- Added typed mutation returning with scalar/DTO list, single, and optional
  single terminals; upsert supports RETURNING/OUTPUT on the three providers.
- MySQL/Firebird explicit-target upsert
  and automatic upsert generated-value hydration remain unsupported.

## 0.2.0 - 2026-08-26

SnapData 0.2.0 is the first stable release set. All four packages share the same
version and support .NET 8 and .NET 10; .NET 9 applications use the compatible
.NET 8 assemblies.

### Migration tooling

- Added the `SnapData.Migrations.Cli` global tool.
- Added `init`, `add`, `list`, `status`, `script`, `migrate`, and `rollback`
  workflows.
- Added C# migration project scaffolding and safe migration registration.
- Added project-free SQL migration workspaces with paired Up/Down files or a
  combined `-- snapdata:down` format.
- Added pending deployment script export with atomic writes and overwrite
  protection.
- Added `--no-color`, redirected-output detection, and `NO_COLOR` support for
  CI environments.
- Added migration history validation, statement fingerprints, provider locking,
  previews, status reporting, and opt-in rollback.

### Schema and providers

- Added the portable migration planning API and DDL compilers for SQLite, SQL
  Server, PostgreSQL, MySQL, and Firebird.
- Added read-only schema inspectors and a shared schema model for all five
  providers.
- Added conditional and asynchronous migration planning against live schema.

### SnapData core

- Added strongly typed entity queries, projections, joins, predicates,
  aggregates, subqueries, pagination, and relation includes.
- Added entity and fluent insert, update, and delete APIs with generated-value
  hydration.
- Added raw SQL, stored procedure requests, reusable commands, transactions,
  and command observers.

### Compatibility note

This is a stable NuGet release, but the project remains pre-1.0. Minor `0.x`
versions may refine public APIs; patch releases remain backward compatible.
