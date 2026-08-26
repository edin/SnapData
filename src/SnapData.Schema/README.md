# SnapData.Schema

Read-only database schema inspection for [SnapData](https://github.com/edin/SnapData).

`SnapData.Schema` exposes one schema model and a common inspector API for
SQLite, SQL Server, PostgreSQL, MySQL, and Firebird. Applications that only use
SnapData for queries and commands do not need this optional package.

## Installation

```powershell
dotnet add package SnapData.Schema
```

Install the application's ADO.NET provider separately. The package targets .NET
8 and .NET 10; .NET 9 applications use the compatible .NET 8 assembly.

## Quick start

```csharp
using Microsoft.Data.Sqlite;
using SnapData;
using SnapData.Schema;

var database = new SnapDatabase(
    SqliteFactory.Instance,
    "Data Source=app.db",
    SqliteQueryCompiler.Instance);

ISchemaInspector inspector = new SqliteSchemaInspector(database);

if (await inspector.TableExistsAsync(new SchemaObjectName("users")))
{
    var users = await inspector.GetTableAsync(new SchemaObjectName("users"));
    foreach (var column in users!.Columns)
    {
        Console.WriteLine(
            $"{column.Name}: {column.StoreType}, nullable={column.IsNullable}");
    }
}
```

An inspector can instead receive an `IDbExecutor`, such as an open SnapData
session or transaction, when inspection must use an existing connection context.

## Providers

| Database | Inspector |
|---|---|
| SQLite | `SqliteSchemaInspector` |
| SQL Server | `SqlServerSchemaInspector` |
| PostgreSQL | `PostgresSchemaInspector` |
| MySQL | `MySqlSchemaInspector` |
| Firebird | `FirebirdSchemaInspector` |

Every inspector implements `ISchemaInspector`:

```csharp
var tableName = new SchemaObjectName("orders", "sales");
var exists = await inspector.TableExistsAsync(tableName);
var hasTotal = await inspector.ColumnExistsAsync(tableName, "total");
var objects = await inspector.GetObjectsAsync(schema: "sales");
var table = await inspector.GetTableAsync(tableName);
var schema = await inspector.ReadAsync();
```

All operations are asynchronous, accept cancellation tokens, and only read
database metadata. They do not create or alter schema objects.

## Schema model

`ReadAsync` returns an immutable snapshot rooted at `DatabaseSchema`:

- `DatabaseSchema` contains tables and views.
- `TableSchema` contains columns, a primary key, foreign keys, indexes, and
  optional definition SQL.
- `ViewSchema` contains columns and optional definition SQL.
- `ColumnSchema` exposes the provider store type, normalized `DbType`, CLR type,
  nullability, default expression, generated kind, and SQLite type affinity.
- `SchemaObjectName` keeps the object name and optional schema separate.

Provider metadata differs, so properties that cannot be determined are nullable
or empty rather than guessed.

## Controlling inspection

Use `SchemaReadOptions` when a complete snapshot would retrieve more metadata
than needed:

```csharp
var schema = await inspector.ReadAsync(new SchemaReadOptions
{
    IncludeColumns = true,
    IncludePrimaryKeys = true,
    IncludeForeignKeys = false,
    IncludeIndexes = false,
    IncludeViews = false,
    IncludeDefinitionSql = false
});
```

The same options can be passed to `GetTableAsync`.

[`SnapData.Migrations`](https://github.com/edin/SnapData/tree/main/src/SnapData.Migrations)
uses this package for conditional migration planning and history validation.
Schema inspection can also be used independently.

SnapData is distributed under the MIT License.
