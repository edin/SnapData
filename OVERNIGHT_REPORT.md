# SnapData 0.4 overnight handoff

Completed 2026-09-16, before the 08:00 Europe/Sarajevo deadline. The agreed
implementation scope is complete; this is a reviewable working tree, not a
published release. Existing changes were preserved. No commits, pushes, tags,
project-version changes, credential changes, or NuGet publication were made.

## Implemented

- Explicit-target single-row upsert on SQLite, PostgreSQL, and SQL Server,
  with selected update columns, do-nothing, returning, and live contention tests.
- Compiler capabilities and provider-bound session/transaction `Compile`/`Build`.
- Typed mutation `Returning<TResult>` with list/single/optional terminals.
- Explicit default-only inserts and generated-only entity inserts; MySQL
  computed-value hydration is rejected before writing rather than misreported.
- Mapped `InsertManyAsync` with bounded enumeration, parameter/row budgets,
  default atomic execution, caller-owned transactions, explicit non-atomic
  mode, and documented counts. No batch input hydration.
- Typed expression assignments and consistent typed insert `Values` replacement.
- Transaction/session cleanup hardening, original operation-error preservation
  in internal batch/opening cleanup, and rejection of uncertain session reuse.
- CLI export fixes for trailing comments and stale empty exports, explicit
  history/locking warnings, and no-color confirmation behavior.
- Packed-package consumer checks, custom executor/compiler compatibility,
  provider support matrix, changelog, and implementation-plan updates.

## Final verification

| Suite | .NET 8 | .NET 10 |
| --- | ---: | ---: |
| Core | 211 | 211 |
| Migrations | 122 | 122 |
| Schema | 30 | 30 |
| CLI | 92 | 92 |
| Integration | 66 | 66 |
| Total | 521 | 521 |

1,042 Release test executions passed with no failures/skips. Integration test
classes ran serially to avoid the known concurrent schema-DDL inspection race;
explicit lock-contention tests still exercised concurrent writers internally.
Tests used SQLite and the configured PostgreSQL, SQL Server, MySQL, and Firebird
test databases, with their test objects cleaned up afterward.

All four projects packed successfully. The combined `eng/verify-packages.ps1`
entry point passed installed CLI C# and project-free SQL workflows, followed by
real package-referenced API consumers on .NET 8.0.30 and .NET 10.0.11. Local feed
mapping and fresh caches prevented published packages from substituting for the
working-tree build. `git diff --check` passed.

Local artifacts, still carrying the unchanged **0.2.0** package version:

- Packages: `artifacts/overnight-final-20260916/`
- Installed test tool: `artifacts/overnight-final-tool-20260916/`
- Package API consumer: `eng/package-api-smoke/`

These are verification artifacts, not a 0.4 release. Temporary CLI workspaces
were removed; isolated package caches and the tool installation were retained.

## Deliberate limitations and review points

- MySQL/Firebird explicit-target upsert is deferred for 0.4. The support
  decision and provider references are in `API_PLAN.md`; do not advertise
  universal upsert support.
- Per-column SQL DEFAULT, key-only helpers, native bulk loading, bulk upsert,
  and automatic batch/upsert entity hydration remain deferred. Later 0.6
  extensibility, converters, streaming, and lifecycle hooks were not added.
- The pre-existing PostgreSQL schema inspector/concurrent DDL race is **not
  fixed**. The serial integration pass does not prove concurrent schema
  inspection safe. Review this separately before claiming that guarantee.
- Returning means provider-returned rows, not a portable affected-row outcome.
  SQL Server trigger restrictions and older Firebird singleton/null-row
  returning behavior are documented in the README.
- Cardinality/mapping errors may occur after a write; use caller-owned
  transactions when necessary. Cleanup errors after commit do not undo the
  commit. Application-level using-body/disposal failures require explicit
  handling if both exceptions must be retained.
- Interactive selection is unit-tested and redirected CLI behavior is covered;
  no real terminal keypress acceptance test was automated.

## Next human step

Review the working-tree diff in the slices listed in `API_PLAN.md`, then decide
whether to commit and prepare a 0.4.0 release. Rerun packaging with the chosen
release version only when that version change is approved. No release is
authorized by the completed development checklist.

Detailed run history: `OVERNIGHT_PROGRESS.md`.
