using SnapData.Migrations;

namespace SnapData.Migrations.Tests;

public sealed class MigrationScriptTests
{
    [Fact]
    public void Script_preserves_statement_boundaries_and_order()
    {
        var script = new MigrationScript(
            "001-users",
            MigrationDirection.Up,
            [
                new MigrationStatement("CREATE TABLE users (id INTEGER)"),
                new MigrationStatement("CREATE INDEX ix_users_id ON users (id)")
            ]);

        Assert.Equal("001-users", script.MigrationId);
        Assert.Equal(MigrationDirection.Up, script.Direction);
        Assert.Equal(
            [
                "CREATE TABLE users (id INTEGER)",
                "CREATE INDEX ix_users_id ON users (id)"
            ],
            script.Statements.Select(statement => statement.Sql));
    }

    [Fact]
    public void Statement_inputs_are_snapshotted()
    {
        var statements = new List<MigrationStatement>
        {
            new("select 1")
        };
        var script = new MigrationScript("001-query", MigrationDirection.Down, statements);

        statements.Add(new MigrationStatement("select 2"));

        Assert.Equal(["select 1"], script.Statements.Select(statement => statement.Sql));
    }

    [Fact]
    public void Statements_and_scripts_render_without_changing_sql()
    {
        var first = new MigrationStatement("create table users (id integer);");
        var second = new MigrationStatement("create index ix_users_id on users (id);");
        var script = new MigrationScript("001-users", MigrationDirection.Up, [first, second]);

        Assert.Equal("create table users (id integer);", first.ToString());
        Assert.Equal(
            $"create table users (id integer);{Environment.NewLine}create index ix_users_id on users (id);",
            script.ToString());
    }

    [Fact]
    public void Invalid_statements_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new MigrationStatement(""));
        Assert.Throws<ArgumentException>(() => new MigrationStatement(" "));
    }

    [Fact]
    public void Invalid_scripts_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new MigrationScript("", MigrationDirection.Up, [new MigrationStatement("select 1")]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MigrationScript("001", (MigrationDirection)99, [new MigrationStatement("select 1")]));
        Assert.Throws<ArgumentNullException>(() =>
            new MigrationScript("001", MigrationDirection.Up, null!));
        Assert.Throws<ArgumentException>(() =>
            new MigrationScript("001", MigrationDirection.Up, []));
        Assert.Throws<ArgumentException>(() =>
            new MigrationScript("001", MigrationDirection.Up, [null!]));
    }
}
