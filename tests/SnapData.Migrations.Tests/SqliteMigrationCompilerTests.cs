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
                CONSTRAINT "FK_accounts_roles" FOREIGN KEY ("tenant_id", "role_id") REFERENCES "roles" ("tenant_id", "id") ON UPDATE CASCADE ON DELETE SET NULL,
                CONSTRAINT "CK_accounts_balance" CHECK ("balance" >= 0)
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
            table.Check("CK_children_valid", "1 = 1");
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

        verify.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('parents') " +
            "WHERE name IN ('tenant_id', 'id') AND \"notnull\" = 1";
        Assert.Equal(2L, await verify.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Create_table_if_not_exists_is_idempotent_with_its_indexes()
    {
        var plan = new MigrationPlan();
        using (var table = plan.CreateTableIfNotExists("users"))
        {
            table.Identity();
            table.String("email", 250);
            table.Index("IX_users_email", "email");
        }

        var script = new SqliteMigrationCompiler().Compile(
            "001", MigrationDirection.Up, plan);

        Assert.StartsWith(
            "CREATE TABLE IF NOT EXISTS \"users\"",
            script.Statements[0].Sql);
        Assert.Equal(
            "CREATE INDEX IF NOT EXISTS \"IX_users_email\" ON \"users\" (\"email\" ASC)",
            script.Statements[1].Sql);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        for (var run = 0; run < 2; run++)
        {
            foreach (var statement in script.Statements)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement.Sql;
                await command.ExecuteNonQueryAsync();
            }
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText =
            "SELECT COUNT(*) FROM sqlite_schema WHERE name IN ('users', 'IX_users_email')";
        Assert.Equal(2L, await verify.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Native_schema_changes_compile_and_execute_on_sqlite()
    {
        var plan = new MigrationPlan();
        using (var table = plan.CreateTable("users"))
        {
            table.Identity();
        }
        using (var table = plan.AlterTable("users"))
        {
            table.String("email", 250).Nullable();
            table.CreateUniqueIndex(null, IndexColumn.Desc("email"));
            table.RenameColumn("email", "contact_email");
        }
        plan.RenameTable("users", "people");
        plan.DropIndex("people", "UX_users_email");
        plan.DropColumn("people", "contact_email");

        var script = new SqliteMigrationCompiler().Compile(
            "001", MigrationDirection.Up, plan);

        Assert.Equal(
            [
                "ALTER TABLE \"users\" ADD COLUMN \"email\" TEXT",
                "CREATE UNIQUE INDEX \"UX_users_email\" ON \"users\" (\"email\" DESC)",
                "ALTER TABLE \"users\" RENAME COLUMN \"email\" TO \"contact_email\"",
                "ALTER TABLE \"users\" RENAME TO \"people\"",
                "DROP INDEX \"UX_users_email\"",
                "ALTER TABLE \"people\" DROP COLUMN \"contact_email\""
            ],
            script.Statements.Skip(1).Select(statement => statement.Sql));

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
            "SELECT COUNT(*) FROM pragma_table_info('people') WHERE name = 'id'";
        Assert.Equal(1L, await verify.ExecuteScalarAsync());
    }

    [Fact]
    public void Table_rebuild_operations_are_rejected_explicitly()
    {
        var columnPlan = new MigrationPlan();
        columnPlan.AlterColumn("users", new ColumnDefinition(
            "name", MigrationColumnType.String, IsNullable: true));
        var addForeignKeyPlan = new MigrationPlan();
        addForeignKeyPlan.AddForeignKey("users", new ForeignKeyDefinition(
            "FK_users_accounts", ["account_id"], "accounts", ["id"]));
        var dropForeignKeyPlan = new MigrationPlan();
        dropForeignKeyPlan.DropForeignKey("users", "FK_users_accounts");
        var setDefaultPlan = new MigrationPlan();
        setDefaultPlan.SetColumnDefault("users", "active", true);
        var dropDefaultPlan = new MigrationPlan();
        dropDefaultPlan.DropColumnDefault("users", "active");
        var addCheckPlan = new MigrationPlan();
        addCheckPlan.AddCheck(
            "users", new CheckConstraintDefinition("CK_users_valid", "1 = 1"));
        var dropCheckPlan = new MigrationPlan();
        dropCheckPlan.DropCheck("users", "CK_users_valid");

        foreach (var plan in new[]
                 {
                     columnPlan,
                     addForeignKeyPlan,
                     dropForeignKeyPlan,
                     setDefaultPlan,
                     dropDefaultPlan,
                     addCheckPlan,
                     dropCheckPlan
                 })
        {
            var exception = Assert.Throws<NotSupportedException>(() =>
                new SqliteMigrationCompiler().Compile("001", MigrationDirection.Up, plan));
            Assert.Contains("table-rebuild", exception.Message);
        }
    }

    [Fact]
    public void Add_column_rejects_constraints_that_sqlite_cannot_add()
    {
        var plan = new MigrationPlan();
        plan.AddColumn("users", new ColumnDefinition(
            "code", MigrationColumnType.String, IsUnique: true));

        var exception = Assert.Throws<NotSupportedException>(() =>
            new SqliteMigrationCompiler().Compile("001", MigrationDirection.Up, plan));

        Assert.Contains("ADD COLUMN", exception.Message);
    }

    [Fact]
    public void Conditional_column_sql_is_visible_and_changes_the_fingerprint()
    {
        var conditional = new MigrationPlan();
        using (var table = conditional.AlterTable("users"))
        {
            table.IfNotExists().String("email");
        }
        var unconditional = new MigrationPlan();
        using (var table = unconditional.AlterTable("users"))
        {
            table.String("email");
        }

        var conditionalScript = new SqliteMigrationCompiler().Compile(
            "001", MigrationDirection.Up, conditional);
        var unconditionalScript = new SqliteMigrationCompiler().Compile(
            "001", MigrationDirection.Up, unconditional);

        Assert.StartsWith(
            "/* SnapData: IF COLUMN NOT EXISTS users.email */",
            Assert.Single(conditionalScript.Statements).Sql);
        Assert.NotEqual(conditionalScript.Fingerprint, unconditionalScript.Fingerprint);
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
            table.Check("CK_accounts_balance", "\"balance\" >= 0");
            table.Index(null, "name", IndexColumn.Desc("created_at"));
            table.Unique("UX_accounts_tenant_name", "tenant_id", "name");
        }
        return plan;
    }
}
