# SnapData.Migrations

Database migrations for [SnapData](https://github.com/edin/SnapData).

It provides migration plans, a compact schema-building API, provider-specific
DDL compilation, migration history, locking, execution, status inspection, and
rollback using SnapData.

Migration compilers and execution support are available for SQLite, SQL Server,
PostgreSQL, MySQL, and Firebird. Applications can use this package directly, or
use the [`SnapData.Migrations.Cli`](../SnapData.Migrations.Cli/README.md) tool to
build and run migration bundles from the command line.
