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

## Commands

```shell
snapdata list
snapdata status
snapdata script
snapdata migrate
snapdata migrate --yes
snapdata rollback
snapdata rollback --steps 2
snapdata rollback --yes
```

- `list` discovers migrations without connecting to the database.
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
