# SnapData.Schema

Optional database schema inspection for
[SnapData](https://github.com/edin/SnapData).

It provides a common schema model and read-only inspectors for SQLite, SQL
Server, PostgreSQL, MySQL, and Firebird. `SnapData.Migrations` uses the schema
model when migration planning depends on the current database structure.
