# SnapData

Lightweight, explicit data access for .NET 10.

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

SnapData is currently `0.1.0-alpha.1`. The API is usable but may still change
before a stable release.

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
dotnet add package SnapData --prerelease
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
[Table("app.users")]
public sealed class User
{
    [Key]
    [Generated(GeneratedKind.Identity)]
    [Column("user_id")]
    public long Id { get; set; }

    [Column("display_name")]
    public required string Name { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    [Ignore]
    public string DisplayLabel => $"{Id}: {Name}";
}
```

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

## Raw SQL

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
- Migrations or schema management
- Identity resolution across separate relation queries
- Client-side expression evaluation

Applications can build their own contexts, repositories, and query scopes using
ordinary C# composition and extension methods.
