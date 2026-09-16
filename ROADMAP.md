# SnapData roadmap

This roadmap organizes the work toward SnapData 1.0 around coherent, useful
releases. It is a direction rather than a promise: priorities can change as the
packages are used in real applications.

The [API review and implementation plan](API_PLAN.md) records the current
working-tree inventory, concrete 0.4 slices, acceptance criteria, and deferred
API candidates. Use it to distinguish new work from existing foundations.

## Version policy

- Minor `0.x.0` releases represent meaningful feature or design milestones and
  may contain breaking API refinements before 1.0.
- Patch releases such as `0.4.1` contain compatible fixes.
- Intermediate builds remain in GitHub Actions artifacts rather than being
  published to NuGet.org.
- Milestone numbers may be skipped; every published version must only move
  forward.
- `0.9.0` begins the public API freeze. After `1.0.0`, breaking public API
  changes require a new major version.

Expected progression:

```text
0.2.0 -> 0.4.0 -> 0.6.0 -> 0.8.0 -> 0.9.0 -> 1.0.0
```

## 0.2.0: migration tooling

Release goal: users can comfortably create, inspect, apply, and roll back
migrations using either C# bundles or project-free SQL files.

- Add `snapdata init --sql`.
- Add `snapdata add <name>` for SQL migrations.
- Add `snapdata add <name>` for C# migrations.
- Scaffold paired and combined Up/Down SQL formats.
- [x] Add `snapdata script --output <file>`.
- [x] Add plain/no-color output for CI environments.
- [x] Complete package and CLI documentation and examples.
- [x] Pack, install, and validate all four packages as one release set.
- [x] Prepare release notes and verify a clean installation.
- Publish the release notes with the tagged GitHub release.

## 0.4.0: data-writing improvements

Release goal: common write patterns work consistently without requiring raw,
provider-specific SQL.

- Add a provider-independent upsert API.
  - [x] First slice: explicit conflict keys, selected incoming update columns,
    and do-nothing actions in SQL and typed entity builders; SQLite/PostgreSQL
    native compilation, SQLite execution coverage, and explicit rejection on
    unsupported providers.
  - [x] Live PostgreSQL contracts: typed insert/update, composite conflict keys,
    do-nothing, unrelated constraint failures, transaction rollback, and
    verified lock contention between concurrent writers for update/skip actions.
  - [ ] Extend provider support with documented concurrency/conflict semantics
    and live provider contract tests before calling upsert provider-independent.
    0.4 decision: SQLite/PostgreSQL/SQL Server supported; MySQL/Firebird deferred
    explicitly. Do not advertise universal provider support.
  - [x] SQL Server single-row MERGE/HOLDLOCK, selected-column update/skip, and
    nine live concurrency/failure contracts on both .NET targets.
- [x] Add mapped batched inserts on top of existing multi-row SQL support;
  explicit atomic/caller/non-atomic ownership, parameter budgets, and all five
  provider contracts. Firebird uses single-row fallback; native bulk loading deferred.
- Improve generated-value and `RETURNING`/`OUTPUT` behavior.
  - [x] Typed mutation result terminals and upsert returning for SQLite,
    PostgreSQL, and SQL Server; live PostgreSQL/SQL Server result contracts.
- Extend composite-key CRUD coverage and evaluate key-only convenience APIs;
  mapped composite-key update/delete already exist.
- Support default-only inserts and improve typed write ergonomics.
  - [x] Explicit default-only SQL/typed inserts, generated-only entity inserts,
    metadata-aware MySQL hydration validation, and live default/identity tests
    for PostgreSQL, SQL Server, MySQL and Firebird plus SQLite execution tests.
  - [x] Typed SetExpression and consistent Values(entity) replacement semantics.
- Define consistent affected-row results.
- [x] Harden owned session/transaction cleanup and internal batch cancellation
  failures; document caller-owned disposal and uncertain connection state.
- Expose provider capability information.
  - [x] Compiler-level operation flags and unknown limits; session-bound Build
    now matches execution with an explicit compiler override still available.

## 0.6.0: mapping and extensibility

Release goal: applications can customize behavior while entities remain
ordinary classes and no base model type is required.

- Add query and command interceptors.
- Evaluate optional lifecycle interfaces for auditing and timestamps.
- Improve existing configurable naming conventions and mapping diagnostics.
- Add custom value converters.
- Improve constructor mapping and mapping diagnostics.
- Expand enum, JSON, and provider-specific type support.

## 0.8.0: operational maturity

Release goal: behavior under failure, concurrency, and load is documented and
proven.

- Integrate structured diagnostics and logging.
- Add logging/tracing integrations on top of existing command timing and
  observation hooks.
- Add explicit retry integration points without hidden automatic retries.
- Test migration cancellation, timeouts, concurrency, and failure recovery.
- Test large data sets and provider parameter limits.
- Add performance regression benchmarks.
- Publish the supported database-version matrix.

## 0.9.0: API freeze

Release goal: freeze the public surface intended for 1.0 and focus exclusively
on compatibility, correctness, documentation, and usability.

- Review every public API, namespace, name, and visibility decision.
- Finalize nullability, async, cancellation, and exception conventions.
- Add checked-in API compatibility baselines.
- Complete XML documentation for supported public APIs.
- Internalize accidental public APIs and mark experimental surfaces clearly.
- Publish upgrade guidance.
- Validate realistic applications using only packed NuGet packages.

## 1.0.0: compatibility commitment

Release goal: users can depend on the documented public API until the next
major version.

- Resolve all known critical defects.
- Pass provider contracts on every supported database and .NET target.
- Validate migration workflows across supported providers.
- Verify the four-package dependency graph and reproducible release process.
- Publish semantic-versioning, deprecation, and support policies.
- Publish supported .NET and database versions.
- Complete getting-started, production deployment, troubleshooting, and upgrade
  documentation.
- Exercise the release candidate in at least one real application before the
  final tag.

## Release discipline

Every milestone release should be built from a clean commit and must pass the
full test suite, package all coordinated projects, install the CLI from the
locally produced NuGet packages, and complete its package-based migration smoke
workflow. A `v*` Git tag remains the normal NuGet publication switch.
