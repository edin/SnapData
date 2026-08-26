# Changelog

Notable changes to SnapData are documented here.

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
