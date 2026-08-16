using Microsoft.Data.Sqlite;
using SnapData.Migrations;
using SnapData.Schema;

namespace SnapData.Migrations.Tests;

public sealed class SqliteMigrationCompilerTests
{
    [Fact]
    public void Compiles_supported_operations_in_plan_order()
    {
        var plan = new MigrationPlan();
        plan.ExecuteSql("PRAGMA foreign_keys = ON");
        plan.RenameColumn("user\"accounts", "display_name", "name");
        plan.DropColumn("user\"accounts", "obsolete");
        plan.DropTable("old_users");

        var script = new SqliteMigrationCompiler().Compile(
            "001-users", MigrationDirection.Up, plan);

        Assert.Equal(
            [
                "PRAGMA foreign_keys = ON",
                "ALTER TABLE \"user\"\"accounts\" RENAME COLUMN \"display_name\" TO \"name\"",
                "ALTER TABLE \"user\"\"accounts\" DROP COLUMN \"obsolete\"",
                "DROP TABLE \"old_users\""
            ],
            script.Statements.Select(statement => statement.Sql));
    }

    [Fact]
    public void Create_table_compiles_constraints_defaults_and_separate_indexes()
    {
        var plan = AccountsPlan();

        var script = new SqliteMigrationCompiler().Compile(
            "001-accounts", MigrationDirection.Up, plan);

        Assert.Equal(3, script.Statements.Count);
        Assert.Equal(
            """
            CREATE TABLE "user""accounts" (
                "id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "name" TEXT NOT NULL DEFAULT 'O''Brien',
                "active" INTEGER NOT NULL DEFAULT 1,
                "balance" REAL NOT NULL DEFAULT 12.5,
                "created_at" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "tenant_id" INTEGER NOT NULL,
                "role_id" INTEGER NOT NULL,
                CONSTRAINT "FK_accounts_roles" FOREIGN KEY ("tenant_id", "role_id") REFERENCES "roles" ("tenant_id", "id") ON UPDATE CASCADE ON DELETE SET NULL
            )
            """.ReplaceLineEndings(),
            script.Statements[0].Sql.ReplaceLineEndings());
        Assert.Equal(
            "CREATE INDEX \"IX_user\"\"accounts_name_created_at\" ON \"user\"\"accounts\" (\"name\" ASC, \"created_at\" DESC)",
            script.Statements[1].Sql);
        Assert.Equal(
            "CREATE UNIQUE INDEX \"UX_accounts_tenant_name\" ON \"user\"\"accounts\" (\"tenant_id\" ASC, \"name\" ASC)",
            script.Statements[2].Sql);
    }

    [Fact]
    public void Composite_primary_keys_are_table_constraints()
    {
        var plan = new MigrationPlan();
        using (var table = plan.CreateTable("memberships"))
        {
            table.Int64("tenant_id").PrimaryKey();
            table.Int64("user_id").PrimaryKey();
        }

        var sql = new SqliteMigrationCompiler()
            .Compile("001", MigrationDirection.Up, plan).Statements[0].Sql;

        Assert.Contains("PRIMARY KEY (\"tenant_id\", \"user_id\")", sql);
        Assert.DoesNotContain("\"tenant_id\" INTEGER PRIMARY KEY", sql);
    }

    [Fact]
    public async Task Compiled_create_table_and_indexes_execute_on_sqlite()
    {
        var plan = new MigrationPlan();
        using (var table = plan.CreateTable("parents"))
        {
            table.Int64("tenant_id").PrimaryKey();
            table.Int64("id").PrimaryKey();
        }
        using (var table = plan.CreateTable("children"))
        {
            table.Identity();
            table.Int64("tenant_id");
            table.Int64("parent_id");
            table.String("label").Unique();
            table.ForeignKey(
                null, ["tenant_id", "parent_id"], "parents", ["tenant_id", "id"],
                onDelete: ReferentialAction.Cascade);
            table.Index("IX_children_parent", "tenant_id", "parent_id");
        }

        var script = new SqliteMigrationCompiler().Compile("001", MigrationDirection.Up, plan);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var statement in script.Statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement.Sql;
            await command.ExecuteNonQueryAsync();
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText =
            "SELECT COUNT(*) FROM sqlite_schema WHERE name IN ('parents', 'children', 'IX_children_parent')";
        Assert.Equal(3L, await verify.ExecuteScalarAsync());
    }

    [Fact]
    public void Invalid_identity_definitions_are_rejected()
    {
        var plan = new MigrationPlan();
        using (var table = plan.CreateTable("users"))
        {
            table.Int64("id").Identity();
        }

        Assert.Throws<InvalidOperationException>(() =>
            new SqliteMigrationCompiler().Compile("001", MigrationDirection.Up, plan));
    }

    [Fact]
    public void Raw_sql_boundaries_are_preserved()
    {
        var plan = new MigrationPlan();
        plan.ExecuteSql("select 1");
        plan.ExecuteSql("select 2; select 3");

        var script = new SqliteMigrationCompiler().Compile(
            "001-queries", MigrationDirection.Down, plan);

        Assert.Equal(["select 1", "select 2; select 3"],
            script.Statements.Select(statement => statement.Sql));
    }

    private static MigrationPlan AccountsPlan()
    {
        var plan = new MigrationPlan();
        using (var table = plan.CreateTable("user\"accounts"))
        {
            table.Identity();
            table.String("name", 150).Default("O'Brien");
            table.Boolean("active").Default(true);
            table.Decimal("balance").Default(12.5m);
            table.DateTime("created_at").DefaultSql("CURRENT_TIMESTAMP");
            table.Int64("tenant_id");
            table.Int64("role_id");
            table.ForeignKey(
                "FK_accounts_roles",
                ["tenant_id", "role_id"],
                "roles",
                ["tenant_id", "id"],
                onUpdate: ReferentialAction.Cascade,
                onDelete: ReferentialAction.SetNull);
            table.Index(null, "name", IndexColumn.Desc("created_at"));
            table.Unique("UX_accounts_tenant_name", "tenant_id", "name");
        }
        return plan;
    }
}
