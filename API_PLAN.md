# SnapData API review and implementation plan

Reviewed: 2026-09-05. Scope: the current working tree, including the uncommitted
upsert implementation and PostgreSQL integration tests. This is primarily a
review of `src/SnapData`, not an exhaustive audit of every schema/migration API.
Proposed names below are design sketches, not implemented public contracts.

Implementation update (2026-09-16): SQL Server upsert and live contention tests,
compiler capabilities/provider-bound Build, and typed mutation returning have
been added. The returning spelling chosen is
`Returning<TResult>(propertyExpressions).SingleOrDefaultAsync()`. Original
review findings below are retained as rationale; OVERNIGHT_PROGRESS.md records
current implementation/test status and remaining work.

01:00 overnight update: default-only inserts, metadata-aware generated insert
planning, MySQL computed-hydration rejection, typed expression assignment, and
typed Values replacement are implemented and tested. Per-column SQL DEFAULT,
automatic update hydration, and key-only convenience helpers remain deferred;
typed returning provides explicit computed-value reads after updates.

02:00 overnight update: mapped `InsertManyAsync<T>` now provides bounded lazy
enumeration, mapped/generated exclusions, owned atomic transactions by default,
caller-owned transactions, explicit non-atomic execution, and successful-call
counts. SQLite and live PostgreSQL/SQL Server/MySQL/Firebird contracts pass.
Firebird uses single-row fallback; no batch generated-value hydration is claimed.

03:00 update: transaction/session disposal attempts all owned cleanup, preserves
the first failure, attaches secondary cleanup exceptions, and faults sessions
after failed transaction cleanup. Owned batching and connection opening preserve
their original operation errors. Packed consumer examples now exercise both
.NET targets, custom compiler hooks and the pre-0.4 custom executor surface.

### Provider support decision for 0.4

Advertise explicit-target upsert only on SQLite, PostgreSQL, and SQL Server.
MySQL and Firebird remain explicitly unsupported for this operation in 0.4;
this is not a blocker for the remaining APIs or a claim those databases cannot
support other forms of upsert.

- MySQL's native duplicate-key update can react to any primary/unique conflict,
  not the selected `OnConflict` key. Substituting it would weaken our contract,
  particularly on tables with multiple unique keys. A future separately named
  provider-specific operation may expose those semantics deliberately.
  [MySQL reference](https://dev.mysql.com/doc/refman/8.0/en/insert-on-duplicate.html).
- Firebird's `UPDATE OR INSERT ... MATCHING` shares its supplied value list for
  insertion/updating and can match multiple rows; it is not a direct replacement
  for independently selected update columns and a do-nothing action. A future
  MERGE-based implementation needs its own uniqueness, contention, cancellation,
  and provider-version contracts. We have not established those guarantees and
  will not emulate them with check-then-insert.
  [Firebird reference](https://www.firebirdsql.org/file/documentation/chunk/en/refdocs/fblangref50/fblangref50-dml-update-or-insert.html).

## Direction

Make common data-access workflows complete without turning SnapData into a
change-tracking ORM. Keep ordinary entity classes, explicit writes, visible
transactions, and raw SQL as an escape hatch. Finish gaps between existing SQL
builders and typed wrappers before adding unrelated abstractions.

The 0.4 milestone is write-side completeness. It does not require every item in
this document. Later candidates remain optional until an application needs them.

## What already exists

| Area | Existing functionality | Actual remaining gap |
| --- | --- | --- |
| Entity CRUD | Insert/update/delete, mapped columns, composite-key update/delete | Key-only convenience operations; broader provider contracts |
| Generated values | `InsertAsync(entity)` hydrates writable generated properties | Typed mutation results, update/upsert generated values, provider limitations |
| Multiple inserts | `Sql.InsertInto(...).Rows(...)` | Mapped batched execution with bounded parameters and transaction rules |
| Upsert | Explicit/composite targets, selected incoming update columns, do-nothing; SQLite/PostgreSQL | Remaining providers, result retrieval, additional concurrency/failure contracts |
| Queries | Joins, grouping, aggregates, subqueries, projection, paging, first/single/count/any | Streaming, typed expression coverage, selected wrapper inconsistencies |
| Arithmetic updates | Low-level `.Set("balance", Exp.Col("balance") + amount)` | Typed expression-valued assignment; arithmetic itself is not missing |
| Relations | Batched reference/collection `Include` | Composite relationship keys, nested/filtered loading if needed |
| Mapping | Attributes, configurable table/column/key conventions, constructor binding, enum conversion | Converter extension point, diagnostics, broader type coverage |
| Execution | Explicit sessions/transactions, cancellation, command timeout | Failure-path hardening; optional savepoints/external transaction adoption |
| Diagnostics | `ICommandObserver`, timings, failures, affected/result counts | Logging/tracing adapters and deliberate interception semantics |
| Stored procedures | Typed results, multiple result sets, output parameters | Do not rebuild this as a supposedly missing feature |

Evidence: [executor API](src/SnapData/Execution/IDbExecutor.cs),
[entity command factory](src/SnapData/Persistence/EntityCommandFactory.cs),
[SQL mutations](src/SnapData/Querying/MutationBuilders.cs),
[typed mutations](src/SnapData/Querying/EntityMutationQueries.cs),
[query terminals](src/SnapData/Querying/EntityQuery.cs),
[row mapper](src/SnapData/Internal/RowMapper.cs),
[mapping options](src/SnapData/Mapping/MappingOptions.cs),
[observers](src/SnapData/Execution/CommandObservation.cs),
[stored procedures](src/SnapData/Persistence/StoredProcedures.cs).

## 0.4 slices, in implementation order

### 1. Finish the upsert provider contract

Current SQLite and PostgreSQL support stays intact. Next implement SQL Server;
evaluate MySQL and Firebird separately rather than assuming identical semantics.

- Preserve explicit conflict keys and selected update columns.
- Define which primary/unique constraints each implementation can target.
- Test competing writers on both absent and existing rows, commit/rollback of
  the first writer, cancellation while blocked, and unrelated constraint errors.
- Keep transaction ownership with the caller when a transaction is already open.
- Reject unsupported semantics explicitly; do not silently substitute a
  check-then-insert sequence or weaken the selected conflict target.
- Preserve provider affected-row counts; do not infer insert/update outcomes
  from them without a documented, tested provider contract.

Done when each advertised provider/action has live tests and documented limits.
If a provider cannot honor the shared contract, record an explicit limitation
or separately named provider-specific operation before release.

Evidence: [upsert builder](src/SnapData/Querying/UpsertQueryBuilder.cs),
[provider compilers](src/SnapData/Querying/Compilers/ProviderQueryCompilers.cs),
[PostgreSQL contracts](tests/SnapData.IntegrationTests/PostgresUpsertTests.cs).

### 2. Expose capabilities and make SQL inspection use the execution provider

This is a small foundation for result retrieval and batching, not a large
runtime schema-discovery system.

- Add operation-level capability information: explicit-target upsert,
  do-nothing, insert/update/delete/upsert returning, and relevant batch limits.
- Distinguish compiler support from server-version/configuration requirements.
  Allow unknown limits or explicit overrides rather than inventing constants.
- Make session-bound `Build()` use the session compiler by default, retaining
  an explicit compiler override. Today typed wrappers forward a null compiler
  to the ANSI default; a PostgreSQL upsert can execute but its parameterless
  `Build()` throws. Inspecting SQL should match execution.
- Avoid exposing raw connections just to obtain these capabilities.

Done when session and transaction inspection produce the same SQL as execution,
and capability declarations have matching support/rejection tests.

Evidence: [typed upsert Build](src/SnapData/Querying/EntityUpsert.cs),
[typed query Build](src/SnapData/Querying/EntityQuery.cs),
[compiler contract](src/SnapData/Querying/IQueryCompiler.cs).

### 3. Add typed mutation returning

Low-level insert/update/delete `Returning(...)` already exists and can be read
through executor query methods. Typed mutation wrappers currently expose only
`ExecuteAsync()`. Upsert does not yet expose returning at either level.

- Add selected mapped return columns and scalar/DTO result mapping.
- Reuse a small mutation-result abstraction across insert/update/delete/upsert;
  do not attach query filters or paging to an already constructed mutation.
- Define zero/one/many-row terminal behaviour, especially do-nothing upserts.
- Distinguish returning a new result from mutating the input entity.
- Clarify that partial returned columns do not represent a fully hydrated entity.
- For providers without the requested result operation, fail before writing or
  expose a documented alternative with explicit transaction requirements.

Candidate usage: `...Returning(u => u.Id).SingleOrDefaultAsync<long>()`.
Choose exact projection and terminal signatures before implementation.

Done when generated identity/computed values, zero-row outcomes, update/delete
results, transaction execution, and rejected operations have provider contracts.

### 4. Complete generated/default-value writes

- Add explicit `DefaultValues()` for an insert with no supplied columns, and
  make entity inserts with only generated properties follow a defined path.
- Support refreshing generated values after an update, preferably by reusing
  slice 3 rather than introducing a separate result-mapping implementation.
- Distinguish omitted columns, explicit null, and SQL `DEFAULT`; do not treat
  null as permission to omit a supplied value.
- Audit generated-value plans by category. The current MySQL plan accepts one
  generated column and uses `LAST_INSERT_ID()`; identity and arbitrary computed
  properties must not be advertised as equivalent.
- Document what happens to hydrated in-memory values if a surrounding
  transaction later rolls back. No implied entity change tracking.

Done when default-only entities and generated-value categories are tested or
explicitly rejected before execution on each supported provider.

Evidence: [entity insert validation](src/SnapData/Persistence/EntityCommandFactory.cs),
[generated insert execution](src/SnapData/Execution/DbExecutor.cs).

### 5. Add mapped batched inserts

Candidate: `InsertManyAsync<T>(IEnumerable<T>, InsertManyOptions, ...)`.

- Reuse mapping and existing multi-row SQL support where available.
- Bound row/parameter counts; avoid materializing the entire input by default.
- Specify empty input, input enumeration failure, cancellation, and partial
  completion behaviour.
- Decide atomicity explicitly: an owned transaction for atomic mode versus
  caller-owned transaction versus a clearly requested non-atomic mode.
- Start without generated-value hydration across a batch. Never associate
  returned IDs with input order unless the provider contract guarantees it.
- Report counts with documented meaning; do not call this provider-native bulk
  loading or imply a particular performance gain without measurement.

Done when mapped/generated exclusions, batch boundaries, parameter limits,
rollback, and cancellation are tested. Native bulk loaders and bulk upsert can
be separate future slices.

### 6. Close small typed write ergonomics gaps

- Add expression-valued typed assignment, such as an explicitly designed
  `SetExpression(...)` or `Increment(...)`, reusing existing SQL expressions.
  Do not overload arbitrary object values ambiguously with SQL expressions.
- Consider a key-only lookup/delete helper with a deliberate composite-key
  representation; current predicate-based workflows remain valid.
- Define and align builder reuse: low-level insert `Values()` replaces values,
  while typed insert currently adds them; `.Value()` rejects duplicates.
- Keep mapped key/generated-column protections and whole-table mutation guards.

Typed assignment is the priority here. Key helpers may move out of 0.4 if the
key representation needs a larger mapping design.

## 0.4 reliability and release gate

These are correctness work, not new API surface:

- [x] Fix SQL export statement termination after trailing line comments.
- [x] Separate no-color output from interactive input support.
- [x] Define safe empty-export replacement instead of preserving stale SQL.
- [x] Warn that raw exported migration SQL does not update migration history.
- [x] Harden transaction disposal after rollback/connection failure; preserve
  original failures and release resources even if rollback itself fails.
- [x] Verify new APIs forward cancellation/options and emit existing observer
  events correctly, including result-returning operations and batches.
- [x] Run contract tests against advertised providers and both .NET targets
  (66 integration contracts per target, executed class-by-class; concurrent
  schema-DDL inspector robustness is not claimed).
- [x] Compile realistic examples against packed packages, including transaction
  and custom `IDbExecutor`/compiler implementations where the contract changes.
- [x] Update public API docs, changelog, supported-operation matrix, and package
  smoke tests; publish only after explicit release approval.

The original document-only review has now been followed by implementation and
verification. OVERNIGHT_PROGRESS.md is the source for exact test runs and known
limitations; the checkboxes do not authorize publishing.

## Later candidates: 0.6 and beyond

| Candidate | Why / scope | Acceptance focus |
| --- | --- | --- |
| Explicit optimistic concurrency | Expected version/original values for update/delete; no hidden tracking | Two writers, stale values, affected-row interpretation, computed tokens |
| Streaming queries | `IAsyncEnumerable<T>` instead of always buffering `QueryAsync<T>` | Early disposal, cancellation, borrowed connection ownership, active reader limits; define or reject `Include` |
| Typed predicate additions | Whitelist string methods, collection `Contains`, nullable operations | Literal wildcard escaping, null semantics, empty/large sets, unsupported expressions; no arbitrary expression execution |
| Custom value converters | Shared parameter and row conversion for value objects/provider types | Round trips, nulls, enum/date/time/JSON cases, constructor/property parity |
| Mapping diagnostics | Improve existing constructor binding and naming conventions | Ambiguous constructors, absent/duplicate columns, projection strictness; avoid silent data loss |
| Composite relationship keys | Extend the current one-local/one-foreign-key relation model | Tuple matching, nullable keys, parameter-aware batching |
| Nested/filtered includes | Only when real usage requires them | Predictable query counts and no accidental N+1 queries |
| Logging/tracing adapters | Build on command observers already present | Redaction, duration, cancellation, exception policy |
| Optional lifecycle/interceptor interfaces | Auditing/timestamps without mandatory base classes | Ordering, async/cancellation, exactly-once expectations; observers are not mutable interceptors |
| Savepoints/external transactions | Only with clear ownership contracts | Commit/rollback/disposal responsibilities, provider support |

Evidence: [expression translator](src/SnapData/Querying/EntityExpressionTranslator.cs),
[relation metadata](src/SnapData/Mapping/RelationAttribute.cs),
[relation loaders](src/SnapData/Querying/RelationLoaders.cs),
[transaction session](src/SnapData/Execution/DbTransactionSession.cs).

## Deliberately not required

No mandatory `Model` base class, automatic change tracker, ambiguous `SaveAsync`
that guesses insert versus update, silent retries, automatic schema discovery
on every query, or universal repository abstraction. CTEs, window functions,
set operations, native bulk loaders, and raw-SQL multi-result convenience APIs
need demonstrated demand before becoming release requirements.

## Immediate next task

The scoped 0.4 implementation passed final Release/package verification and is
ready for human review; see OVERNIGHT_REPORT.md. Do not extend into the later API backlog or publish without
explicit approval. MySQL/Firebird upsert, per-column DEFAULT, key helpers, and
batch/upsert entity hydration are deliberately deferred. Keep the known
concurrent schema-inspection limitation visible in release review.
