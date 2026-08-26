# SnapData.Migrations

Provider-aware database migrations for [SnapData](https://github.com/edin/SnapData).

`SnapData.Migrations` provides ordered migration bundles, a compact schema API,
provider-specific SQL compilation, previews, history validation, locking,
transactional execution, status inspection, and opt-in rollback. It supports
SQLite, SQL Server, PostgreSQL, MySQL, and Firebird.

## Installation

```powershell
dotnet add package SnapData.Migrations
```

Install the application's ADO.NET provider separately. The package targets .NET
8 and .NET 10; .NET 9 applications use the compatible .NET 8 assembly.

For most projects, the easiest workflow is the
[`SnapData.Migrations.Cli`](https://github.com/edin/SnapData/tree/main/src/SnapData.Migrations.Cli)
tool. It can initialize, create, list, inspect, script, apply, and roll back both
C# and project-free SQL migrations.

## Define a migration

```csharp
using SnapData.Migrations;

public sealed class CreateUsers : Migration
{
    public override string Id => "202608260001_create_users";

    public override void Up(MigrationPlan migration)
    {
        using var table = migration.CreateTable("users");
        table.Identity();
        table.String("name", 150);
        table.Boolean("active").Default(true);
        table.Timestamps();
        table.Unique("UX_users_name", "name");
    }

    public override void Down(MigrationPlan migration) =>
        migration.DropTable("users");
}
```

Migration IDs must be unique and are applied in bundle order. Sortable IDs such
as `yyyyMMddHHmmss_name` make that order obvious in source control.

The plan API covers tables, columns, indexes, foreign keys, check constraints,
defaults, renames, provider branches, and raw SQL. Its portable column types
include integers, strings, text, booleans, decimals, floating point, GUIDs,
binary data, dates, times, date/times, date/time offsets, and JSON.

## Register migrations

```csharp
public sealed class AppMigrationBundle : MigrationBundle
{
    protected override void Configure(MigrationCollection migrations)
    {
        migrations
            .Add<CreateUsers>()
            .Add<AddUserEmail>();
    }
}
```

`MigrationCollection` can also add instances or ranges and scan selected types.
Explicit registration is recommended for predictable review and ordering.

## Create a runner

```csharp
using Microsoft.Data.Sqlite;
using SnapData;
using SnapData.Migrations;

var database = new SnapDatabase(
    SqliteFactory.Instance,
    "Data Source=app.db",
    SqliteQueryCompiler.Instance);

var bundle = new AppMigrationBundle();
var runner = new MigrationRunner(
    database,
    bundle,
    SqliteMigrationDialect.Instance);
```

| Database | Dialect |
|---|---|
| SQLite | `SqliteMigrationDialect.Instance` |
| SQL Server | `SqlServerMigrationDialect.Instance` |
| PostgreSQL | `PostgresMigrationDialect.Instance` |
| MySQL | `MySqlMigrationDialect.Instance` |
| Firebird | `FirebirdMigrationDialect.Instance` |

## Preview, inspect, and apply

```csharp
var status = await runner.GetStatusAsync();
foreach (var entry in status)
{
    Console.WriteLine($"{entry.MigrationId}: {entry.State}");
}

var scripts = await runner.PreviewAsync();
foreach (var script in scripts)
{
    Console.WriteLine($"-- {script.MigrationId}");
    foreach (var statement in script.Statements)
    {
        Console.WriteLine(statement.Sql);
    }
}

await runner.MigrateAsync();
```

`GetStatusAsync` is read-only and does not create the history table. Applying a
migration creates it when needed and executes each migration in a transaction.
`MigrateToAsync(id)` applies through a selected migration; it never moves a
database backward.

History records include an SHA-256 fingerprint of compiled statements. Changed,
missing, and out-of-order migrations are rejected before execution.
Schema-dependent plans are reported as unverifiable rather than assigned a
misleading fingerprint comparison.

## Conditional and asynchronous migrations

Conditional builders support operations such as adding a column only when it is
missing. For decisions that require direct inspection, override `UpAsync` or
`DownAsync`; `MigrationContext` exposes the plan (including its provider name),
a read-only schema helper, and the cancellation token:

```csharp
public override async ValueTask UpAsync(MigrationContext context)
{
    if (!await context.Schema.ColumnExistsAsync("users", "email"))
    {
        using var table = context.Plan.AlterTable("users");
        table.String("email", 250).Nullable();
    }
}
```

Schema-dependent plans use the same session or transaction as the runner.

## Rollback

Rollback is disabled by default and must be enabled deliberately:

```csharp
var runner = new MigrationRunner(
    database,
    bundle,
    SqliteMigrationDialect.Instance,
    new MigrationRunnerOptions
    {
        RollbackPolicy = MigrationRollbackPolicy.Enabled
    });

await runner.RollbackAsync(steps: 1);
```

Every selected migration must provide a valid `Down` plan. Rollback runs newest
first and removes history only after the database transaction succeeds.

## Locking

`MigrationLocking.Required` is the default. SQLite, SQL Server, PostgreSQL, and
MySQL provide built-in migration locks. Firebird has no built-in native lock, so
applications must supply `MigrationRunnerOptions.MigrationLock` or explicitly
choose `MigrationLocking.Disabled`. `Optional` uses a lock when available and
otherwise proceeds without one.

Lock timeout, lease duration, resource name, history table name, and rollback
policy are configured through `MigrationRunnerOptions`.

## SQL migrations

The library can represent raw SQL directly:

```csharp
var migration = new SqlMigration(
    "202608260002_seed_roles",
    "INSERT INTO roles (name) VALUES ('admin')",
    "DELETE FROM roles WHERE name = 'admin'");
```

For a project-free folder of `.sql` files, use the CLI. It supports paired
`id.sql` / `id.down.sql` files or a combined file separated by
`-- snapdata:down`.

## CLI workflow

```powershell
dotnet tool install --global SnapData.Migrations.Cli

snapdata init
snapdata add CreateOrders
snapdata status
snapdata script --output deployment.sql
snapdata migrate --yes
```

Use `snapdata init --sql` for a workspace that does not require a .NET project.
See the full
[CLI guide](https://github.com/edin/SnapData/tree/main/src/SnapData.Migrations.Cli)
and the runnable
[SQLite example](https://github.com/edin/SnapData/tree/main/examples/SqliteMigrations).

SnapData is distributed under the MIT License.
