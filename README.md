# SnapData

Lightweight, explicit data access for .NET 8, .NET 9, and .NET 10.

SnapData sits between raw ADO.NET and a full ORM. It keeps SQL visible, maps
rows to strongly typed results, provides entity CRUD, and offers a compact query
AST for code that needs composition. There is no change tracker, `DbContext`, or
repository abstraction.

```csharp
var books = await session
    .From<Book>()
    .Where(book => book.Published)
    .OrderBy(book => book.Title)
    .ToListAsync();
```

Projection queries remain close to SQL:

```csharp
var rows = await session
    .From<Book>("b")
    .Join("Authors a ON a.Id = b.AuthorId")
    .Select<BookWithAuthor>(
        "b.Id",
        "b.Title",
        "a.Name AS AuthorName")
    .ToListAsync();
```

## Status

SnapData `0.2.0` is the first stable package set. The API may still evolve
between minor `0.x` releases as the project progresses toward 1.0.

See the [changelog](CHANGELOG.md) for release notes and the
[release roadmap](ROADMAP.md) for planned milestones toward 1.0.

The package contains native `net8.0` and `net10.0` assemblies. .NET 9
applications use the compatible `net8.0` assembly through NuGet's normal
framework selection.

SnapData is distributed under the MIT License.

Created and designed by Edin, developed in collaboration with OpenAI Codex.

Supported query compilers:

| Provider | Compiler | Integration tested |
|---|---|---:|
| SQLite | `SqliteQueryCompiler` | Yes |
| SQL Server | `SqlServerQueryCompiler` | Yes |
| PostgreSQL | `PostgresQueryCompiler` | Yes |
| MySQL | `MySqlQueryCompiler` | Yes |
| Firebird | `FirebirdQueryCompiler` | Yes |

The integration contract covers sessions, transactions, CRUD, projections,
joins, grouping, subqueries, pagination, and common database types on all five
providers.

## Installation

```powershell
dotnet add package SnapData
```

Install the ADO.NET provider used by the application separately, for example:

```powershell
dotnet add package Microsoft.Data.Sqlite
dotnet add package Microsoft.Data.SqlClient
dotnet add package Npgsql
dotnet add package MySqlConnector
dotnet add package FirebirdSql.Data.FirebirdClient
```

For a runnable end-to-end example using SQLite, see
[`samples/SnapData.Sample`](samples/SnapData.Sample), or run:

```powershell
dotnet run --project samples/SnapData.Sample
```

### Schema inspection and migrations

SnapData keeps schema tooling optional. Install only the companion packages an
application needs:

```powershell
dotnet add package SnapData.Schema
dotnet add package SnapData.Migrations
dotnet tool install --global SnapData.Migrations.Cli
```

`SnapData.Schema` reads database metadata, while `SnapData.Migrations` provides
provider-specific migration planning, history, locking, execution, and rollback.
The `snapdata` tool can initialize a migration project and then list, inspect,
script, apply, or roll back its migrations:

```powershell
snapdata init
snapdata list
snapdata status
snapdata status --no-color
snapdata script
snapdata script --output deployment.sql
snapdata migrate --yes
```

The CLI can also run ordered `.sql` files directly from a configured `Scripts`
directory, so deployments do not require a migration project or compiled
assembly. Up and Down SQL can use paired files or sections in one file.

See the [migration library guide](src/SnapData.Migrations/README.md), the
[CLI guide](src/SnapData.Migrations.Cli/README.md), and the runnable
[SQLite migration example](examples/SqliteMigrations/README.md).

## Creating a database

An adapter binds connection creation to the compiler for that provider:

```csharp
var database = new SnapDatabase(
    SqliteFactory.Instance,
    "Data Source=app.db",
    SqliteQueryCompiler.Instance);

await using var session = await database.OpenSessionAsync();
```

Other providers use the same shape:

```csharp
var sqlServer = new SnapDatabase(
    SqlClientFactory.Instance,
    sqlServerConnectionString,
    SqlServerQueryCompiler.Instance);

var postgres = new SnapDatabase(
    NpgsqlFactory.Instance,
    postgresConnectionString,
    PostgresQueryCompiler.Instance);

var mysql = new SnapDatabase(
    MySqlConnectorFactory.Instance,
    mysqlConnectionString,
    MySqlQueryCompiler.Instance);

var firebird = new SnapDatabase(
    FbProviderFactory.Instance,
    firebirdConnectionString,
    FirebirdQueryCompiler.Instance);
```

Applications with custom connection creation can implement `IDatabaseAdapter`
or construct `DatabaseAdapter` directly.

### Borrowing a connection

SnapData can borrow an existing connection without taking ownership:

```csharp
await using var session = database.Borrow(connection);
```

Disposing the session does not dispose a borrowed connection. If SnapData had to
open a closed borrowed connection, it closes it when the session is disposed.

## Entity mapping

Mapping is convention-based and refined through attributes:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("app.users")]
public sealed class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("user_id")]
    public long Id { get; set; }

    [Column("display_name")]
    public required string Name { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    [NotMapped]
    public string DisplayLabel => $"{Id}: {Name}";
}
```

SnapData uses the standard .NET Data Annotations for tables, columns, keys,
ignored properties, and generated values. Entity classes already annotated for
other .NET data tooling can therefore reuse the same mapping metadata.

Schema can be expressed inline or separately:

```csharp
[Table("app.users")]              // compact form
// or: [Table("users", Schema = "app")]
```

Without attributes:

- The CLR type name becomes the table name.
- Property names become column names.
- `Id` or `{EntityName}Id` becomes the conventional key.
- Public readable properties are mapped unless ignored or marked as relations.

Custom conventions are supported:

```csharp
var mappings = new EntityMappingProvider(new MappingOptions
{
    TableName = type => ToSnakeCase(type.Name),
    ColumnName = property => ToSnakeCase(property.Name)
});

var database = new SnapDatabase(adapter, mappings);
```

Mappings are immutable and cached. `EntityMapping` exposes the table, keys,
properties, generated fields, and relations discovered for a type.

## Entity CRUD

```csharp
await session.InsertAsync(user);

user.Name = "Updated";
await session.UpdateAsync(user);

await session.DeleteAsync(user);
```

Insert excludes identity and computed properties. Update uses all mapped keys
and writable fields. Delete uses the mapped keys. Keyless entities cannot be
updated or deleted.

Writable generated fields are hydrated after inserts on every supported
provider. SQLite, PostgreSQL, and Firebird use `RETURNING`; SQL Server uses
`OUTPUT INSERTED`; and MySQL retrieves a single generated identity with
`LAST_INSERT_ID()` on the same session connection.

## Typed entity queries

`From<T>()` uses the mapped table and returns complete `T` instances:

```csharp
var users = await session
    .From<User>()
    .Where(user => user.Active && user.CreatedAt >= since)
    .OrderByDescending(user => user.Name)
    .Limit(20)
    .ToListAsync();
```

Common expression trees are translated into the SnapData predicate AST:

- Comparisons and captured values
- Boolean properties and negation
- `&&` and `||`
- Null checks
- Property-to-property comparisons
- Typed ordering and grouping

Unsupported expression nodes fail explicitly. Use `Exp` or parsed SQL criteria
for more advanced conditions.

### Aliases and source overrides

```csharp
session.From<User>()                 // mapped source
session.From<User>("u")              // mapped source with alias
session.From<User>().As("u")         // mapped source with alias
session.From<User>(Sql.Table("app.users").As("u")) // explicit source override
```

The string passed to `From<T>(string)` is an alias, not a table name. Use a
`TableReference` for a full source override. Both alias forms qualify mapped
selections, typed predicates, and typed ordering.

## Projections and joins

`From<TSource>()` selects the mapped source while `Select<TResult>()` changes
the returned row shape:

```csharp
public sealed record UserRole(long Id, string Name, string? RoleName);

var rows = await session
    .From<User>("u")
    .LeftJoin("roles r ON r.id = u.role_id")
    .Select<UserRole>("u.id", "u.name", "r.name AS RoleName")
    .OrderBy("u.id")
    .ToListAsync();
```

Available joins are `Join`, `InnerJoin`, `LeftJoin`, `RightJoin`, `FullJoin`,
and `CrossJoin`.

Entity references provide a fully typed alternative when mapped column names
and aliases should be checked by the compiler:

```csharp
var books = session.Entity<Book>("b");
var authors = session.Entity<Author>("a");

var rows = await session
    .From(books)
    .Join(
        authors,
        books.Col(book => book.AuthorId) == authors.Col(author => author.Id))
    .Where(books.Col(book => book.Published) == true)
    .Select<BookWithAuthor>(
        books.Col(book => book.Id),
        books.Col(book => book.Title),
        authors.Col(author => author.Name, "AuthorName"))
    .OrderBy(books.Col(book => book.Title))
    .ToListAsync();
```

The second `Col` argument is a projection alias. The equivalent
`authors.Col(author => author.Name).As("AuthorName")` form remains available.
Structured columns can also be passed to `GroupBy` and
`OrderByDescending`.

Compact references such as `"app.users u"`, `"u.id"`, and
`"u.name AS UserName"` are parsed into structured table and column nodes. The
equivalent explicit API is useful for reusable references:

```csharp
var users = Sql.Table("app.users").As("u");
var roles = Sql.Table("app.roles").As("r");

var query = Sql
    .Select(
        users.Col("id"),
        users.Col("name"),
        roles.Col("name").As("RoleName"))
    .From(users)
    .LeftJoin(roles, users.Col("role_id") == roles.Col("id"));

var rows = await session.QueryAsync<UserRole>(query);
```

## Predicates

The expression builder parameterizes supplied values:

```csharp
var predicate =
    (Exp.Col("active") == true)
    & (Exp.Col("created_at") >= since)
    & Exp.Col("status").NotIn("deleted", "blocked");

var users = await session
    .From<User>()
    .Where(predicate)
    .ToListAsync();
```

Column expressions support:

```csharp
column.IsNull();
column.IsNotNull();
column.Like("Ed%");
column.NotLike("Disabled%");
column.StartsWith("Ed");
column.EndsWith("@example.com");
column.Contains("search");
column.In(1, 2, 3);
column.NotIn(1, 2, 3);
column.Between(10, 20);
column.NotBetween(10, 20);
```

Combine predicates with `&`, `|`, `.Not()`, or `Exp.Not(predicate)`.

### Parsed criteria

SQL-like criteria strings are scanned and converted to the same AST:

```csharp
var users = await session
    .From<User>(Sql.Table("users").As("u"))
    .Where(
        "u.active = @active AND u.created_at >= @since",
        new { active = true, since })
    .ToListAsync();
```

The parser supports qualified columns, named parameters, literals, comparisons,
`NOT`, `AND`, `OR`, parentheses, `IS NULL`, `LIKE`, `BETWEEN`, and `IN`.
Values are emitted as command parameters rather than copied into SQL.

`SqlParser.ParseCriteria` and `SqlParser.ParseJoin` are available when a
standalone AST is needed.

## Aggregates and grouping

```csharp
public sealed record CustomerSummary(
    long CustomerId,
    long OrderCount,
    decimal Total);

var count = Sql.Count("o.id");

var summaries = await session
    .From("orders o")
    .Select<CustomerSummary>(
        Sql.Col("o.customer_id").As("CustomerId"),
        count.As("OrderCount"),
        Sql.Sum("o.total").As("Total"))
    .GroupBy("o.customer_id")
    .Having(count > 1)
    .ToListAsync();
```

Available aggregates are `Count`, `Sum`, `Avg`, `Min`, and `Max`.

```csharp
Sql.Count("customer_id").DistinctValues().As("CustomerCount")
```

Queries also support `.Distinct()` and typed grouping:

```csharp
session.From<User>().GroupBy(user => user.Active);
```

## Subqueries

```csharp
var qualifyingUsers = Sql.From("orders o")
    .Select("o.user_id")
    .Where(Exp.Col("o.total") > 100);

var users = await session
    .From<User>(Sql.Table("users").As("u"))
    .Where(Exp.Col("u.id").In(qualifyingUsers))
    .ToListAsync();
```

Correlated existence checks are also supported:

```csharp
var auditExists = Sql.From("audits a")
    .Select("a.id")
    .Where(Exp.Col("a.user_id") == Exp.Col("u.id"));

var users = await session
    .From<User>(Sql.Table("users").As("u"))
    .Where(Exp.Exists(auditExists))
    .ToListAsync();
```

Use `Exp.NotExists(subquery)` or `column.NotIn(subquery)` for the negated forms.
Nested queries share the outer parameter context, preventing parameter-name
collisions. An `IN` subquery must select exactly one expression.

## Terminal operations and pagination

```csharp
await query.ToListAsync();
await query.FirstAsync();
await query.FirstOrDefaultAsync();
await query.SingleAsync();
await query.SingleOrDefaultAsync();
await query.AnyAsync();
await query.CountAsync();
```

These terminals are available on both mapped entity queries and
`Select<TResult>()` projections.

Strict terminals throw when no row exists. `SingleAsync` and
`SingleOrDefaultAsync` also throw when more than one row is returned.

```csharp
PageResult<User> page = await session
    .From<User>()
    .Where(user => user.Active)
    .OrderBy(user => user.Id)
    .PageAsync(pageNumber: 2, pageSize: 20);
```

`PageResult<T>` exposes `Items`, `TotalCount`, `PageNumber`, `PageSize`,
`TotalPages`, `HasPreviousPage`, and `HasNextPage`. Pagination clones the query,
so the original builder remains reusable.

## Row locking and atomic increments (0.4.0)

MySQL and PostgreSQL queries can lock selected rows through `ForUpdate()` on
SQL, source, entity and projected builders. Execute the query through an explicit
transaction; the database releases locks when that transaction commits or rolls
back. Locks are retained by first/single terminal query clones and projections.
Other built-in compilers throw `NotSupportedException`; check
`session.Capabilities.ForUpdate` when selecting a provider-dependent workflow.

```csharp
await using var transaction = await session.BeginTransactionAsync();
var user = await transaction.From<User>()
    .Where(user => user.Id == userId)
    .ForUpdate()
    .SingleOrDefaultAsync();
// Read and modify protected state through this transaction.
await transaction.CommitAsync();
```

`ForUpdate(false)` disables locking on a builder. Locking is rejected for
`DISTINCT`, grouped/aggregate selections, and `CountAsync`, `AnyAsync` or
`PageAsync`; use a row-returning terminal. This API does not add skip-locked,
nowait, table-specific lock targets or SQL Server lock hints. Joined queries
remain subject to each database's locking restrictions.

Typed numeric assignments support `Increment` with either one or an explicit
parameterized amount. The addition happens in the database, avoiding a
read/modify/write race. Normal key/generated-property and mutation-predicate
guards still apply; numeric overflow follows the database's rules.

```csharp
await transaction.Update<User>()
    .Increment(user => user.Version)
    .Where(user => user.Id == userId)
    .ExecuteAsync();

await transaction.Update<Account>()
    .Increment(account => account.Balance, 2.5m)
    .Where(account => account.Id == accountId)
    .ExecuteAsync();
```

`Increment` accepts non-nullable numeric properties. For more general SQL
assignments, `SetExpression(property, valueExpression)` remains available.

## Relations and includes

Reference and collection navigations can be loaded using split queries:

```csharp
public sealed class User
{
    [Key]
    public long Id { get; init; }

    public long? AddressId { get; init; }

    [Relation(nameof(AddressId), nameof(Address.Id))]
    public Address? Address { get; set; }

    [Relation(nameof(Id), nameof(Order.UserId))]
    public List<Order> Orders { get; init; } = [];
}

var users = await session
    .From<User>()
    .Include(user => user.Address)
    .Include(user => user.Orders)
    .ToListAsync();
```

SnapData collects distinct non-null keys and loads related entities in batches
of 500 using the same session or transaction. Relation properties are excluded
from normal columns and mutations. Collection results are grouped by their
foreign key, and parents without matching rows receive an empty collection.
Collection navigations may use `List<T>`, `IList<T>`, `ICollection<T>`, or
`IReadOnlyList<T>`. SnapData populates an existing mutable collection when
possible and otherwise assigns a new `List<T>` through a writable property.

## Insert, update, and delete builders

Typed builders use entity mappings and expression trees:

```csharp
await session
    .InsertInto<User>()
    .Value(user => user.Name, "Edin")
    .Value(user => user.Active, true)
    .ExecuteAsync();

await session
    .Update<User>()
    .Set(user => user.Name, "SnapData")
    .Where(user => user.Id == userId)
    .ExecuteAsync();

await session
    .DeleteFrom<User>()
    .Where(user => user.Id == userId)
    .ExecuteAsync();
```

The lower-level AST builders remain useful for dynamic or unmapped tables:

```csharp
await session.ExecuteAsync(
    Sql.InsertInto("users")
        .Values(new { name = "Edin", active = true }));

await session.ExecuteAsync(
    Sql.Update("users")
        .Set("attempts", Exp.Col("attempts") + 1)
        .Set("updated_at", Exp.RawValue("CURRENT_TIMESTAMP"))
        .Where(Exp.Col("id") == id));

await session.ExecuteAsync(
    Sql.DeleteFrom("users")
        .Where(Exp.Col("id") == id));
```

Updates and deletes require a predicate. Whole-table mutations must be made
explicit:

```csharp
await session.ExecuteAsync(
    Sql.DeleteFrom("temporary_users").AllRows());
```

`Returning(...)` compiles to the provider-specific mutation result syntax where
supported, including SQL Server's `OUTPUT` form.

## Upsert (0.4.0)

Upsert inserts one row, or handles a conflict with an explicitly named primary
key or unique constraint. Choose the columns to copy from the incoming row:

```csharp
await session.ExecuteAsync(
    Sql.UpsertInto("users")
        .Values(new { email = "edin@example.com", name = "Edin", active = true })
        .OnConflict("email")
        .UpdateColumns("name", "active"));

await session.UpsertInto<User>()
    .Values(user)
    .OnConflict(u => u.Email)
    .UpdateColumns(u => u.Name, u => u.Active)
    .ExecuteAsync();
```

These examples assume `users.email` has a unique constraint and `User.Email`
maps to that column. Composite targets accept multiple columns or property
expressions. Use `.DoNothing()` instead of `.UpdateColumns(...)` to leave an
existing row untouched. Both the conflict target and action are required;
SnapData never implicitly updates every property. The last action selected
replaces the previous one.

SQLite and PostgreSQL use a single native `INSERT ... ON CONFLICT` statement.
SQL Server uses single-row `MERGE ... WITH (HOLDLOCK)` with no delete action;
it neither starts nor commits a separate transaction. The conflict columns
must be backed by an ordinary primary key or unique constraint. SQL Server
uses null-safe key equality to match its unique-constraint semantics. Filtered
indexes and expression conflict targets are not supported. No preliminary
existence query is issued. ANSI, MySQL, and Firebird compilers currently throw
`NotSupportedException` for upsert. Provider semantics follow
[SQLite UPSERT](https://www.sqlite.org/lang_upsert.html) and
[PostgreSQL INSERT](https://www.postgresql.org/docs/current/sql-insert.html).

SQL Server locking follows [Microsoft's MERGE guidance](https://learn.microsoft.com/en-us/sql/t-sql/statements/merge-transact-sql#concurrency-considerations-for-merge).
This path targets ordinary rowstore tables; specialized table features and
trigger-heavy workloads need application-specific validation. Deadlocks and
other provider failures are surfaced, not silently retried. Cancellation is
forwarded to the provider; SqlClient may report server-side cancellation as
`SqlException` rather than `OperationCanceledException`.

Conflict and update columns must have incoming values. Conflict columns cannot
be updated. Nullable conflict values retain database uniqueness semantics;
they are not automatically treated as equal. Other constraint failures remain
errors, including with `DoNothing()`. Partial/expression conflict targets are
not supported in this slice.

Typed upserts respect mapped insertable/updatable properties and exclude
generated properties. `ExecuteAsync()` returns the provider's affected-row
count, not an inserted-versus-updated classification, and does not hydrate
generated values back into the entity. Transaction sessions expose the same
API; query options and cancellation tokens pass through normal execution.
Bulk upserts and automatic generated-value hydration for upsert are deferred.

The 0.4 support decision is to keep MySQL/Firebird explicit-target upsert
unsupported, not silently weaken `OnConflict` or selected-update semantics.
See [the provider decision](API_PLAN.md#provider-support-decision-for-04) for the
rationale and requirements before extending support.

### Mapped batch inserts (0.4.0)

```csharp
var result = await session.InsertManyAsync(users,
    new InsertManyOptions { BatchSize = 100, MaxParametersPerCommand = 500 },
    new QueryOptions { CommandTimeout = 30 }, cancellationToken);
```

`InsertManyAsync<T>` uses the mapping for `T`, snapshots insertable values, and
consumes input once in bounded batches. Identity/computed properties are excluded;
input entities are not hydrated. Empty input returns zero counts without opening
a connection. Generated-only entities use one default insert per input item.

The defaults are 100 rows and 500 parameters per statement, further reduced by
advertised compiler limits. These are budgets, not live server discovery: lower
them for a more restrictive server/connection. SQL Server caps VALUES inserts at
1,000 rows; Firebird uses single-row inserts. Unsupported standalone Firebird
multi-row VALUES inserts now fail during compilation.

`Atomic = true` owns and commits a transaction when called on a session. Database,
enumeration, or cancellation failures roll back owned work. Inside an existing
transaction, the caller retains commit/rollback responsibility; no savepoint is
created. Explicit `Atomic = false` on a session allows completed batches to remain
after a later failure. Errors propagate without a partial-result object. Enumeration
must not execute other commands on the same session while its owned transaction
is active. Transactional behavior also depends on the provider/table engine.

Successful calls return `RowsSubmitted` (input rows sent), `StatementsExecuted`
(insert commands), and `AffectedRows` (sum of provider counts, or null if any count
was unknown). Affected rows are not a portable insert/update classification.
Timeouts, cancellation, and command observers use the existing execution path.
This is batched INSERT, not provider-native bulk loading or bulk upsert.

### Default-only inserts and typed assignments (0.4.0)

```csharp
await session.ExecuteAsync(Sql.InsertInto("audit_entries").DefaultValues());
var id = await session.InsertInto<AuditEntry>()
    .DefaultValues().Returning<long>(entry => entry.Id).SingleAsync();
```

`DefaultValues()` inserts one row without supplying columns. It cannot be mixed
with `Values()`, `Value()`, or `Rows()`. Entities with no insertable properties
also use this path through `InsertAsync(entity)` or typed `.Values(entity)`.
SQLite, PostgreSQL, SQL Server and Firebird use `DEFAULT VALUES`; MySQL uses
the empty column/value-list form. Required fields without defaults still fail
according to the database's rules. Explicit null remains null; it is never
silently converted to an omitted/defaulted column. A portable per-column SQL
`DEFAULT` expression is not added in this slice.

`InsertAsync(entity)` hydrates supported generated properties. MySQL entity
hydration accepts exactly one identity property, rejecting computed properties
before writing. `CompileGeneratedEntityInsert` supplies this mapping metadata
to compilers and bridges to the existing column-only hook for custom compilers.
The low-level column-only hook remains the caller's responsibility.

Generated values returned or assigned before a transaction rollback are not
reset in application objects. For updated computed columns, select them with
typed `Returning<TResult>(...)`; `UpdateAsync(entity)` does not silently refresh
the input entity. Typed insert `Values(entity)` now replaces previously supplied
values, matching the low-level builder; duplicate individual `Value()` calls
still fail.

Typed updates can assign existing SQL expressions without client-side reads:

```csharp
var account = session.Entity<Account>();
await session.Update<Account>()
    .SetExpression(a => a.Balance, account.Col(a => a.Balance) + amount)
    .Where(a => a.Id == accountId)
    .ExecuteAsync();
```

`SetExpression` preserves mapped key/generated-column and whole-table guards.
The selected property is typed, but SQL expression value/type compatibility
remains the caller's responsibility and is ultimately checked by the database.

### Typed mutation results (0.4.0)

Insert, update, delete, and upsert wrappers expose selected returned properties:

```csharp
var id = await session.InsertInto<User>()
    .Values(user)
    .Returning<long>(u => u.Id)
    .SingleAsync();

var optionalId = await session.UpsertInto<User>()
    .Values(user)
    .OnConflict(u => u.Email)
    .DoNothing()
    .Returning<long?>(u => u.Id)
    .SingleOrDefaultAsync();
```

`MutationResult<TResult>` offers `ToListAsync()`, `SingleAsync()`,
`SingleOrDefaultAsync()`, and provider-bound `Build()`. Scalar selections should
select one column; DTO properties/constructor parameters follow normal column
mapping. Returned partial DTOs are not fully populated entities. Input entities
are not hydrated by these terminal methods.

Each terminal call executes the write again; results are not cached. Builders
are mutable, so finish configuration before requesting/executing results.
`SingleOrDefaultAsync()` returns `default(TResult)` for zero rows; use a nullable
scalar type to distinguish no row from zero. `SingleAsync()` buffers returned
rows and requires exactly one. A cardinality/mapping failure happens after the
write: use a caller-owned transaction and roll it back if that should undo the
operation. No implicit rollback or retry is performed.

SQLite, PostgreSQL and SQL Server support upsert returning. SQL Server emits
`OUTPUT INSERTED...`; table/trigger restrictions of direct OUTPUT apply. MySQL
returning is rejected during compilation, before executing a write. Query
options, cancellation, and command-observer events use the normal execution
pipeline. Low-level upsert also supports `.Returning("id", ...)` for use with
executor query methods.

The PostgreSQL upsert integration tests cover real lock contention between two
writers, along with insert/update/skip, composite targets, constraint failures,
and rollback. With `SNAPDATA_POSTGRES_CONNECTION` pointing to a test database,
run them using:

```bash
dotnet test tests/SnapData.IntegrationTests --filter FullyQualifiedName~PostgresUpsertTests
```

The test database user needs permission to create schemas. Each test uses a
uniquely named schema and removes it afterward; these tests skip when the
connection variable is not set.

SQL Server contracts run with `SNAPDATA_SQLSERVER_CONNECTION` and filter
`FullyQualifiedName~SqlServerUpsertTests`. They create uniquely named tables in
`dbo` and remove them afterward. The concurrency tests need visibility of
blocking sessions through `sys.dm_exec_requests` (appropriate server-state
permissions for the server version). Use a dedicated test database.

## Raw SQL

### Provider-bound inspection and capabilities (0.4.0)

The current working-tree support is:

| Operation | SQLite | PostgreSQL | SQL Server | MySQL | Firebird |
| --- | --- | --- | --- | --- | --- |
| Explicit-target upsert: update / do nothing | Yes | Yes | Yes | Unsupported | Unsupported |
| Typed insert/update/delete returning | RETURNING | RETURNING | OUTPUT | Unsupported | RETURNING |
| Upsert returning | RETURNING | RETURNING | OUTPUT | Unsupported | Unsupported |
| Default-only insert | Yes | Yes | Yes | Yes | Yes |
| Mapped batch insert | Multi-row | Multi-row | Multi-row, max 1,000 rows | Multi-row | Single-row fallback |
| Entity insert identity hydration | Yes | Yes | Yes | One identity | Yes |
| Entity insert computed-value hydration | Yes | Yes | Yes | Rejected | Yes |

These are compiler/API capabilities, not guarantees for every server version or
table configuration. Returning rows and triggers have provider restrictions;
for example SQL Server direct OUTPUT has trigger limitations and older Firebird
DSQL returning behavior is singleton-oriented. Firebird 4 can return one row of
nulls when an UPDATE affects no rows, and rejects multi-row UPDATE returning;
SnapData does not normalize those rows into a portable affected-row result.
See the [Firebird 4 RETURNING contract](https://www.firebirdsql.org/file/documentation/chunk/en/refdocs/fblangref40/fblangref40-dml-update.html#fblangref40-dml-update-returning)
and [SQL Server OUTPUT restrictions](https://learn.microsoft.com/en-us/sql/t-sql/queries/output-clause-transact-sql?view=sql-server-ver17).
Unsupported operations fail
explicitly. Returning creates result values; batches/upserts do not automatically
hydrate input entities. Use the tested provider contracts and your target server
version when qualifying an application for release.

`session.Compile(builder)` and parameterless `Build()` on session/transaction
wrappers use the configured compiler without opening a connection. Pass a
compiler explicitly to `Build(compiler)` to override it. Standalone SQL
builders retain their ANSI default.

`session.Capabilities` and `IQueryCompiler.Capabilities` describe compiler
support for upsert, per-operation returning, and multi-row inserts. These are not live server
probes: versions, permissions, constraints, and table features still matter.
Null parameter/row limits mean unknown, not unlimited. Custom compilers default
to advertising no capabilities. Custom `IDbExecutor` implementations should
implement `Compile` for provider-bound inspection; the default throws rather
than silently selecting a different dialect.

Raw SQL remains a first-class path:

```csharp
var users = await session.QueryAsync<User>(
    """
    SELECT id, name, active
    FROM users
    WHERE active = @active
    """,
    new { active = true });

var affected = await session.ExecuteAsync(
    "UPDATE users SET active = @active WHERE id = @id",
    new { id, active = false });

var count = await session.ScalarAsync<long>(
    "SELECT COUNT(*) FROM users");
```

Anonymous objects and `ParameterSet` values are bound as command parameters.
`QueryOptions` currently supports per-command timeouts.

### Observing commands

Implement `ICommandObserver`, or derive from `CommandObserver`, to connect
SnapData to an application's logging or telemetry system:

```csharp
public sealed class QueryLogger : CommandObserver
{
    public override void Executed(CommandExecutedContext context)
    {
        Console.WriteLine(
            $"{context.Kind} {context.Command.CommandText} " +
            $"completed in {context.Duration.TotalMilliseconds:F2} ms");

        foreach (var parameter in context.Command.Parameters)
        {
            Console.WriteLine($"{parameter.Name} = {parameter.Value}");
        }
    }
}

var database = new SnapDatabase(
    adapter,
    commandObserver: new QueryLogger());
```

Contexts contain snapshots, so they may safely be retained or queued after a
callback returns. The snapshot is shallow: a parameter value that is itself a
mutable reference is not cloned. `Executing` is called immediately before the
provider operation; `Executed` includes duration and result information;
`Failed` receives the original exception. Callbacks are synchronous and should
return quickly—enqueue the snapshot when logging work is asynchronous. Observer
failures reported from `Failed` never replace the original database exception.

## Transactions

Transaction sessions expose the same query and mutation APIs and always use the
transaction's acquired connection:

```csharp
await using var transaction = await session.BeginTransactionAsync();

await transaction.InsertAsync(user);
await transaction.InsertAsync(audit);

await transaction.CommitAsync();
```

Call `RollbackAsync()` explicitly when needed. An uncommitted transaction rolls
back when disposed. While a session has an active transaction, commands must be
executed through the transaction object.

Disposal attempts all owned cleanup even if rollback fails; a session whose
transaction cleanup failed rejects further execution because the connection
state is uncertain. Dispose that session and discard/reset the connection
according to its provider (a borrowed, already-open connection remains owned by
the caller). The first cleanup error is rethrown with later errors attached as
`IReadOnlyList<Exception>` in `exception.Data["SnapData.CleanupExceptions"]`;
attached errors can themselves contain cleanup errors.

`InsertManyAsync` and session opening preserve their original operation exception
when internal cleanup also fails. This cannot preserve an arbitrary exception
from application code automatically: C# `using`/`await using` can replace a body
exception if disposal throws. Applications needing both should explicitly catch
the operation and cleanup failures. A cleanup failure after a successful commit
does not mean the committed writes were rolled back; do not retry blindly.

## Stored procedures

Typed procedure requests declare their result through `IStoredProc<TResult>`:

```csharp
[StoredProcedure("dbo.GetOrders")]
public sealed class GetOrders : IStoredProc<Result<OrderDto>>
{
    public int CustomerId { get; init; }
}

Result<OrderDto> result = await session.Query(
    new GetOrders { CustomerId = 42 });

List<OrderDto> orders = result.Items;
```

Public readable request properties become input parameters. Rows are mapped
through the normal result mapper. `QueryProcedureAsync` is the explicitly named
equivalent.

Multiple datasets can be mapped into a custom holder:

```csharp
public sealed class GetOrderDataResult
{
    [ResultSet(0)]
    public List<OrderDto> Orders { get; init; } = [];

    [ResultSet(1)]
    public List<CustomerDto> Customers { get; init; } = [];
}

[StoredProcedure("dbo.GetOrderData")]
public sealed class GetOrderData : IStoredProc<GetOrderDataResult>
{
    public int CustomerId { get; init; }
}
```

Without `[ResultSet]`, public `List<T>` properties use declaration order. When
attributes are present, indexes must be unique and contiguous from zero.

Typed requests support explicit input, output, input/output, and return-value
properties. Output values are written back to the request after execution:

```csharp
[StoredProcedure("dbo.SearchUsers")]
public sealed class SearchUsers : IStoredProc<Result<User>>
{
    [Input("search")]
    public string Search { get; init; } = "";

    [Output("total_count")]
    public int TotalCount { get; set; }
}
```

For lower-level calls, the fluent `Command` API exposes typed references to
output values:

```csharp
var command = Command.StoredProcedure("app.search_users")
    .Input("search", "Ed");

var total = command.Output<int>("total_count");
var returnValue = command.ReturnValue<int>();

var users = await session.QueryAsync<User>(command);
Console.WriteLine(total.Value);
```

`Command.Text(sql)` provides the same parameter API for text commands.
`SetParameter(name, value)` and `Parameter<T>(name).Value` allow a command to
be reused with updated input values. `CommandDefinition` and `ParameterSet`
remain available as the lower-level representation.

Stored-procedure availability and semantics depend on the database. SQLite does
not support stored procedures.

## Testing

Run the unit and available integration tests:

```powershell
dotnet test SnapData.slnx -c Release
```

External provider contracts are enabled with connection strings:

```text
SNAPDATA_SQLSERVER_CONNECTION
SNAPDATA_POSTGRES_CONNECTION
SNAPDATA_MYSQL_CONNECTION
SNAPDATA_FIREBIRD_CONNECTION
```

Provider tests create and remove `contract_users`, `contract_orders`, and
`contract_values` in the configured test database. Always use a dedicated
database such as `SnapDataTests`.

## Benchmarks

The BenchmarkDotNet project compares SnapData and Dapper against a long-lived
SQLite in-memory connection. Setup and mapper warm-up are outside measured
methods.

```powershell
dotnet run -c Release --project benchmarks/SnapData.Benchmarks -- --filter *
```

Hydration can be measured independently from command execution:

```powershell
dotnet run -c Release --project benchmarks/SnapData.Benchmarks -- --filter *HydrationBenchmarks*
dotnet run -c Release --project benchmarks/SnapData.Benchmarks -- --filter *MapperCreationBenchmarks*
```

Use BenchmarkDotNet's short job while iterating:

```powershell
dotnet run -c Release --project benchmarks/SnapData.Benchmarks -- --job short --filter *MutableHydrationBenchmarks*
```

For a very fast smoke check, use the dry job. Its numbers are not suitable for
performance conclusions:

```powershell
dotnet run -c Release --project benchmarks/SnapData.Benchmarks -- --job dry --filter *MutableHydrationBenchmarks*
```

## Design boundaries

SnapData intentionally does not provide:

- Change tracking or a unit of work
- A required `DbContext`
- Repository abstractions
- Schema management requirements in the core `SnapData` package
- Identity resolution across separate relation queries
- Client-side expression evaluation

Applications can build their own contexts, repositories, and query scopes using
ordinary C# composition and extension methods.
