# Overnight implementation progress

Window: 2026-09-15 evening through 2026-09-16 08:00 Europe/Sarajevo.
Automation: `snapdata-overnight-api-implementation` (hourly thread heartbeat).

Final status: scoped implementation and verification completed during the 04:00
run. See OVERNIGHT_REPORT.md for the handoff. Stop further implementation;
the overnight heartbeat is PAUSED because its completion condition was met.

Scope: API_PLAN.md 0.4 slices and reliability gate; deferred 0.6 candidates
remain out of scope. Preserve existing uncommitted work. No commits, pushes,
tags, version bumps, publishing, or credential changes authorized by this run.

## Baseline

Existing SQLite/PostgreSQL upsert, tests, API_PLAN.md, README and ROADMAP edits
were present before this run. Treat them as shared work, not disposable changes.

## Current slice

- SQL Server single-row upsert compilation added using MERGE WITH (HOLDLOCK).
- Explicit conflict keys and update columns retained; do-nothing omits matched
  action. Null-safe equality matches ordinary SQL Server unique-key semantics.
- Verified: nine live SQL Server contracts per target, covering insert/update/
  skip, composite/null keys, unrelated errors, rollback, real concurrent locks,
  first-writer rollback, existing-row contention, and blocked cancellation.
- SQL Server + PostgreSQL upsert contracts: 14 passed per .NET target (28 total).
- Core tests: 135 passed per .NET target (270 total).
- SqlClient can surface attention cancellation as SqlException; documented and
  tested without globally changing existing cancellation exception semantics.
- Test tables were automatically removed. README documents support/limitations.

## Next

1. Record remaining-provider semantic support decisions; MySQL/Firebird remain
   explicitly unsupported until their conflict/concurrency semantics are proven.
2. Capabilities and session-bound Build completed: all seven wrapper types use
   executor compilation unless explicitly overridden. Added conservative custom
   compiler/executor defaults, operation-level returning/upsert flags, and nullable
   unknown batch limits. Core tests: 138 per framework passed (276 total).
3. Typed returning completed for insert/update/delete/upsert with the shared
   MutationResult<TResult> API. Includes list/single/single-or-default/build,
   SQLite/PostgreSQL RETURNING and SQL Server OUTPUT for upsert, and updated
   capability flags. No automatic entity hydration. Tests cover scalar/DTO
   values, skip results, cardinality errors, rollback, cancellation, and observer
   events; live SQL Server/PostgreSQL returning contracts added.
4. 01:00 run: DefaultValues() implemented on SQL/typed insert, generated-only
   entity insert, metadata-aware CompileGeneratedEntityInsert with compatible
   delegation to old custom compiler hooks, and pre-write MySQL computed-value
   hydration rejection. Explicit null/default omission remains distinct.
5. Typed SetExpression and Values(entity) replacement consistency implemented.
   Key/generated protections and whole-table guards preserved. Automatic update
   hydration remains explicit through Returning; no change tracker added.
6. 02:00 run: mapped InsertManyAsync completed with bounded row/parameter budgets,
   lazy single enumeration and value snapshots, default owned atomic mode,
   caller-owned transactions, explicit non-atomic mode, and count results.
   No generated-ID hydration. Firebird falls back to one INSERT per row;
   unsupported standalone multi-row VALUES now fails compilation. SQL Server
   advertises its 1000-row VALUES limit; parameter caps remain unknown and the
   default 500-parameter budget is caller-overridable, not server discovery.
7. CLI reliability: exported delimiters are on their own line (safe even when
   a trailing comment ends with a semicolon); empty exports replace stale SQL
   only with --force and otherwise retain overwrite protection. Export headers
   and CLI README warn about history/locking. No-color no longer disables
   terminal prompts; unavailable interaction yields the explicit --yes error.
8. 03:00 run: transaction/session disposal attempts all owned cleanup, preserves
   the first exception and attaches subsequent errors in
   exception.Data["SnapData.CleanupExceptions"]. Failed transaction cleanup
   faults the parent session. Owned batching and connection opening retain
   original operation/cancellation failures if cleanup also fails. Includes
   sync/async, owned/borrowed connection and post-commit disposal-failure tests.
   C# application-level using bodies still need explicit exception handling if
   both body and disposal fail; no impossible guarantee of preserving an
   arbitrary caller exception. Cleanup failure after commit is not a rollback.
9. Provider decision documented in API_PLAN.md: explicit-target upsert stays
   SQLite/PostgreSQL/SQL Server for 0.4; MySQL/Firebird explicitly unsupported.
   MySQL any-unique-key and Firebird MATCHING semantics are not silently mapped
   to this API. Per-column DEFAULT and key helpers remain deferred.
10. Added eng/verify-api.ps1 + package-api-smoke consumer: actual local nupkg
    references, isolated package cache and source mapping, both .NET targets,
    custom old compiler hooks and pre-0.4 IDbExecutor implementation. UpsertInto
    now has an explicit unsupported interface default to preserve source
    compatibility for existing custom executors. Existing verify-packages.ps1
    invokes API smoke after CLI smoke, refuses preexisting tool directories,
    maps SnapData packages to the local feed, and validates temp cleanup paths.
11. 04:00 run: added 35 command-option/token/observer contracts per target,
    covering every typed returning operation/terminal in sessions/transactions,
    generated/default writes, expression assignments, upsert, batch unknown
    counts, and cancellation between batches. Public support matrix completed,
    including Firebird 4 null-row returning and SQL Server OUTPUT restrictions.
    Final Release verification and the combined packaged CLI/API checks passed.
    Broader provider tests passed class-by-class to avoid the known
    PostgreSQL concurrent schema-DDL inspector race. Do not claim that production
    race is fixed or paper over it by globally disabling test parallelism.

## Latest verification

Final 04:00 run: 211 core + 122 migrations + 30 schema + 92 CLI + 66 integration
tests per target, all Release: 1,042 executions passed on .NET 8/.NET 10, no
failures/skips. Integration classes executed serially with explicit concurrency
tests intact. Final local pack + combined eng/verify-packages.ps1 passed; all
four packages retain version 0.2.0. Final artifacts are in
artifacts/overnight-final-20260916 and artifacts/overnight-final-tool-20260916.
Final API cache: C:/Users/Edin/AppData/Local/Temp/snapdata-api-smoke-71c389aabe2a4505a90dee3b63fee57b.
git diff --check passed. OVERNIGHT_REPORT.md records limitations and the required
human review/release decisions. No implementation remains in the agreed overnight
scope; do not autonomously expand the backlog or publish.

Previous run:

03:00 run: 176 core tests per target; 122 migrations, 30 schema, 92 CLI, plus
all 66 integration contracts per target passed (972 executions across .NET 8
and .NET 10, zero failures/skips). Integration classes ran serially; tests still
exercise deliberate contention internally. The integration project was rebuilt
before the final serial sweep. Earlier selected-write runs also passed.
All four current projects packed locally with unchanged version 0.2.0 into
artifacts/overnight-20260916-0300. Package consumers passed on .NET 8.0.30 and
.NET 10.0.11. Packaged CLI C# and project-free SQL workflows passed from an
isolated NuGet cache; tool retained at artifacts/overnight-tool-20260916-0300.
Generated CLI smoke workspaces were removed; no user files were deleted.
No package was published and no project version changed. API smoke cache retained
at C:/Users/Edin/AppData/Local/Temp/snapdata-api-smoke-9993931b79af4129ab59e334b7df863e.
The later wiring of verify-api into verify-packages was inspected; their two
components have passed independently, and final verification should run the
combined entry point again with a fresh tool directory.

Previous run:

02:00 run: 163 core tests per target, 122 migrations, 30 schema, and 92 CLI.
Selected live tests: 24 per target (upsert, returning, defaults, mapped batching).
All selected runs passed on .NET 8 and .NET 10: 862 executions, zero skips.
Batch contracts cover all five providers, including live PostgreSQL/SQL Server/
MySQL/Firebird success, caller rollback, atomic failure, and non-atomic partial
completion. SQLite adds cancellation, null input, iterator failure/disposal,
mapping exclusions, parameter/row budgets and command observation. SQL export
comment tests execute generated files against SQLite. No full integration-suite
claim: shared schema-inspector concurrency race remains a separate audit item.
Interactive console selection is unit-tested; no real terminal keypress test
was automated. Docs and working tree remain uncommitted; automation stays active.

Previous run:

01:00 run: 150 core tests per target (300 executions), plus 20 live
upsert/returning/default tests per target (40 executions). Default inserts and
identity hydration passed on PostgreSQL, SQL Server, MySQL, Firebird; SQLite is
covered in core tests. Objects created by tests were cleaned up. Full remaining
non-integration regression passed: 122 migrations + 30 schema + 83 CLI per
target. Total this run: 770 non-integration plus 40 live provider executions,
810 passed, zero failures/skips in the selected runs. git diff --check passed.

Previous run:

- Core: 143 tests passed per target, .NET 8 and .NET 10 (286 executions).
- SQL Server/PostgreSQL upsert+returning: 16 passed per target (32 executions).
- Full non-integration regression: 143 core + 122 migrations + 30 schema + 83
  CLI per target, 756 passing executions total. With the 32 live provider
  executions, 788 test executions passed in this slice.
- Scheduled resumptions are at the top of each hour through 08:00 Sarajevo,
  including an 08:00 handoff run. Keep app/computer available for local work.
- MySQL/Firebird explicit-target upsert remains unsupported; do not mark all
  provider coverage done. Capability batch limits remain null/unknown.

At/after the window ends, stop implementation and provide a handoff; pause the
heartbeat if still active. Do not repeatedly retry unchanged external blockers.
