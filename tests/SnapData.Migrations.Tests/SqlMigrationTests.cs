using SnapData.Migrations;

namespace SnapData.Migrations.Tests;

public sealed class SqlMigrationTests
{
    [Fact]
    public void Single_statement_sql_migration_builds_up_and_down_plans()
    {
        var migration = new SqlMigration(
            "001-users",
            "CREATE TABLE users (id INTEGER)",
            "DROP TABLE users");

        var up = new MigrationPlan();
        migration.Up(up);
        var down = new MigrationPlan();
        migration.Down(down);

        Assert.Equal("001-users", migration.Id);
        Assert.Equal(
            "CREATE TABLE users (id INTEGER)",
            Assert.IsType<ExecuteSqlOperation>(Assert.Single(up.Operations)).Sql);
        Assert.Equal(
            "DROP TABLE users",
            Assert.IsType<ExecuteSqlOperation>(Assert.Single(down.Operations)).Sql);
    }

    [Fact]
    public void Multiple_statements_preserve_boundaries_and_order()
    {
        var migration = new SqlMigration(
            "001-users",
            [
                "CREATE TABLE users (id BIGINT)",
                "CREATE INDEX ix_users_id ON users (id)",
                "ALTER TABLE users ADD CONSTRAINT pk_users PRIMARY KEY (id)"
            ]);
        var plan = new MigrationPlan();

        migration.Up(plan);

        Assert.Equal(
            [
                "CREATE TABLE users (id BIGINT)",
                "CREATE INDEX ix_users_id ON users (id)",
                "ALTER TABLE users ADD CONSTRAINT pk_users PRIMARY KEY (id)"
            ],
            plan.Operations.Cast<ExecuteSqlOperation>().Select(operation => operation.Sql));
    }

    [Fact]
    public void Statement_inputs_are_snapshotted()
    {
        var statements = new List<string> { "select 1" };
        var migration = new SqlMigration("001-query", statements);

        statements.Add("select 2");

        Assert.Equal(["select 1"], migration.UpStatements);
    }

    [Fact]
    public void Migration_without_down_statements_is_not_reversible()
    {
        var migration = new SqlMigration("001-users", "create table users (id integer)");

        var exception = Assert.Throws<MigrationNotReversibleException>(() =>
            migration.Down(new MigrationPlan()));

        Assert.Equal("001-users", exception.MigrationId);
    }

    [Fact]
    public void Empty_ids_and_statements_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SqlMigration("", "select 1"));
        Assert.Throws<ArgumentException>(() => new SqlMigration("001", ""));
        Assert.Throws<ArgumentException>(() => new SqlMigration("001", []));
        Assert.Throws<ArgumentException>(() =>
            new SqlMigration("001", ["select 1"], []));
        Assert.Throws<ArgumentException>(() =>
            new SqlMigration("001", ["select 1", " "]));
    }
}
