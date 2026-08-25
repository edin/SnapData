# SnapData Migrations CLI

SnapData's cross-provider command-line tool builds and loads C# migration bundles,
previews provider-specific SQL, reports migration status, and applies pending
migrations.

## Install

```shell
dotnet tool install --global SnapData.Migrations.Cli --version 0.1.0-alpha.4
```

The installed command is `snapdata`.

## Configuration

Create a ready-to-run SQLite migration project in the current directory:

```shell
snapdata init
```

Create a project-free SQLite SQL migration workspace instead:

```shell
snapdata init --sql
```

This creates `snap.ini`, a `Scripts` directory, and two reversible sample
migrations using paired `.sql` and `.down.sql` files.

Add a timestamped migration to the configured SQL workspace:

```shell
snapdata add CreateOrders
```

This creates paired Up and Down files by default. Keep both directions in one
file instead with:

```shell
snapdata add CreateOrders --combined
```

The initializer creates `snap.ini`, a separate `Migrations` project, a bundle,
and two reversible sample migrations. It never overwrites an existing
`snap.ini` or `Migrations` directory.

Create `snap.ini` beside your project or in one of its parent directories:

```ini
[Migration]
Provider=Sqlite
Connection=Data Source=app.db
Project=src/App.Migrations/App.Migrations.csproj
TargetFramework=net8.0
MigrationsNamespace=App.Migrations
Locking=Required
```

Secrets can be read explicitly from environment variables:

```ini
[Migration:production]
Provider=Postgres
Connection=${env.APP_DATABASE_CONNECTION}
```

Select the profile with `--profile production`. Use `--config <file>` to select
a configuration file instead of upward `snap.ini` discovery.

### SQL file migrations

Migration projects are optional. To run migrations directly from SQL files,
configure a scripts directory instead of `Project` or `Assembly`:

```ini
[Migration]
Provider=Sqlite
Connection=Data Source=app.db
Scripts=Scripts
Locking=Required
```

Name each Up migration after its ordered migration ID and optionally provide a
paired Down file:

```text
Scripts/
  20260823120509_create_users.sql
  20260823120509_create_users.down.sql
```

Alternatively, keep both directions in one file using a marker on its own line:

```sql
CREATE TABLE users (id INTEGER PRIMARY KEY);

-- snapdata:down

DROP TABLE users;
```

The base filename is stored as the migration ID. A migration without Down SQL
can be applied but cannot be rolled back. Defining Down SQL both inline and in a
paired file is rejected. Each Up or Down section is submitted to the database as
one raw SQL command; client-only batch separators such as SQL Server `GO` are not
interpreted by SnapData.

## Commands

```shell
snapdata list
snapdata add CreateOrders
snapdata add CreateOrders --combined
snapdata status
snapdata script
snapdata migrate
snapdata migrate --yes
snapdata rollback
snapdata rollback --steps 2
snapdata rollback --yes
```

- `list` discovers migrations without connecting to the database.
- `add` creates a timestamped migration in a configured SQL scripts directory.
- `status` reads migration state without creating history or lock tables.
- `script` previews highlighted SQL for pending migrations without applying it.
- `migrate` validates history, confirms the plan, acquires the configured lock,
  and applies pending migrations.
- `rollback` is an explicitly destructive, development-oriented command. It
  validates history, previews each selected `Down()` plan, asks for confirmation,
  and reverts the latest applied migration by default.

`--yes` skips confirmation and is intended for CI and other non-interactive
environments.

## Locking

`Locking=Required` is the default. `Optional` uses a provider lock when available,
and `Disabled` explicitly runs without one. Firebird has no native migration lock,
so it requires a supplied lock through the library API or explicit
`Locking=Disabled`.

## Exit codes

- `0`: success, including pending status and an already up-to-date database
- `1`: configuration, build, connection, locking, or execution failure
- `2`: modified, missing, or out-of-order migration history
