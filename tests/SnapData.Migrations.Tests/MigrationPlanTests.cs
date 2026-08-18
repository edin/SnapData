using SnapData.Migrations;
using SnapData.Schema;

namespace SnapData.Migrations.Tests;

public sealed class MigrationPlanTests
{
    [Fact]
    public void Manually_created_plans_are_provider_neutral()
    {
        var migration = new MigrationPlan();

        Assert.Null(migration.ProviderName);
        Assert.False(migration.IsProvider(Provider.Sqlite));
        Assert.Throws<ArgumentException>(() => migration.IsProvider(" "));
    }

    [Fact]
    public void Using_scope_builds_an_immutable_create_table_operation()
    {
        var migration = new MigrationPlan();

        using (var table = migration.CreateTable("users"))
        {
            table.Identity();
            table.String("name", 150);
            table.String("email", 250).Unique();
            table.Boolean("active").Default(true);
            table.Timestamps();
            table.Guid("account_id");
            table.Index("IX_users_name", "name");
            table.ForeignKey(
                "FK_users_account",
                ["account_id"],
                "accounts",
                ["id"],
                onDelete: ReferentialAction.Cascade);
            table.Check("CK_users_valid", "1 = 1");
        }

        var operation = Assert.IsType<CreateTableOperation>(
            Assert.Single(migration.Operations));
        Assert.Equal("users", operation.Table);
        Assert.Equal(7, operation.Columns.Count);
        Assert.True(operation.Columns[0].IsIdentity);
        Assert.True(operation.Columns[0].IsPrimaryKey);
        Assert.Equal(150, operation.Columns[1].Length);
        Assert.True(operation.Columns[2].IsUnique);
        Assert.Equal(true, operation.Columns[3].DefaultValue);
        Assert.IsType<SqlDefault>(operation.Columns[4].DefaultValue);
        Assert.True(operation.Columns[5].IsNullable);
        Assert.Equal("IX_users_name", Assert.Single(operation.Indexes).Name);
        Assert.Equal(
            ReferentialAction.Cascade,
            Assert.Single(operation.ForeignKeys).OnDelete);
        Assert.Equal("1 = 1", Assert.Single(operation.Checks).Predicate);
    }

    [Fact]
    public void Create_table_reserves_operation_order_before_disposal()
    {
        var migration = new MigrationPlan();
        var table = migration.CreateTable("users");
        table.Int64("id");
        migration.ExecuteSql("select 1");
        table.Dispose();

        Assert.IsType<CreateTableOperation>(migration.Operations[0]);
        Assert.IsType<ExecuteSqlOperation>(migration.Operations[1]);
    }

    [Fact]
    public void Create_table_if_not_exists_is_preserved_in_the_domain_operation()
    {
        var migration = new MigrationPlan();
        using (var table = migration.CreateTableIfNotExists("users"))
        {
            table.Identity();
        }

        var operation = Assert.IsType<CreateTableOperation>(
            Assert.Single(migration.Operations));
        Assert.True(operation.IfNotExists);
    }

    [Fact]
    public void Schema_change_operations_preserve_their_domain_definitions()
    {
        var migration = new MigrationPlan();
        var email = new ColumnDefinition(
            "email", MigrationColumnType.String, Length: 250);
        var index = new IndexDefinition(
            "IX_users_email", [IndexColumn.Desc("email")], isUnique: true);
        var foreignKey = new ForeignKeyDefinition(
            "FK_users_accounts", ["account_id"], "accounts", ["id"],
            onDelete: ReferentialAction.Cascade);

        migration.RenameTable("old_users", "users");
        migration.AddColumn("users", email);
        migration.AlterColumn("users", email with { IsNullable = true });
        migration.SetColumnDefault("users", "active", true);
        migration.DropColumnDefault("users", "legacy_status");
        migration.CreateIndex("users", index);
        migration.DropIndex("users", "IX_users_email");
        migration.AddForeignKey("users", foreignKey);
        migration.DropForeignKey("users", "FK_users_accounts");

        Assert.Collection(
            migration.Operations,
            operation => Assert.IsType<RenameTableOperation>(operation),
            operation => Assert.Same(email, Assert.IsType<AddColumnOperation>(operation).Column),
            operation => Assert.True(Assert.IsType<AlterColumnOperation>(operation).Column.IsNullable),
            operation => Assert.True(Assert.IsType<SetColumnDefaultOperation>(operation).Value is true),
            operation => Assert.Equal(
                "legacy_status", Assert.IsType<DropColumnDefaultOperation>(operation).Column),
            operation => Assert.Same(index, Assert.IsType<CreateIndexOperation>(operation).Index),
            operation => Assert.IsType<DropIndexOperation>(operation),
            operation => Assert.Same(
                foreignKey, Assert.IsType<AddForeignKeyOperation>(operation).ForeignKey),
            operation => Assert.IsType<DropForeignKeyOperation>(operation));
    }

    [Fact]
    public void Alter_table_uses_change_to_distinguish_add_and_alter_columns()
    {
        var migration = new MigrationPlan();

        using (var table = migration.AlterTable("users"))
        {
            table.String("email", 250).Nullable();
            table.String("name", 150).Nullable().Change();
            table.SetDefault("active", true);
            table.SetDefaultSql("created_at", "CURRENT_TIMESTAMP");
            table.DropDefault("legacy_status");
            table.DropColumn("obsolete");
            table.CreateIndex("IX_users_email", "email");
        }

        Assert.Collection(
            migration.Operations,
            operation =>
            {
                var add = Assert.IsType<AddColumnOperation>(operation);
                Assert.Equal("email", add.Column.Name);
                Assert.Equal(250, add.Column.Length);
                Assert.True(add.Column.IsNullable);
            },
            operation =>
            {
                var alter = Assert.IsType<AlterColumnOperation>(operation);
                Assert.Equal("name", alter.Column.Name);
                Assert.Equal(150, alter.Column.Length);
                Assert.True(alter.Column.IsNullable);
            },
            operation => Assert.True(Assert.IsType<SetColumnDefaultOperation>(operation).Value is true),
            operation => Assert.IsType<SqlDefault>(
                Assert.IsType<SetColumnDefaultOperation>(operation).Value),
            operation => Assert.Equal(
                "legacy_status", Assert.IsType<DropColumnDefaultOperation>(operation).Column),
            operation => Assert.Equal(
                "obsolete", Assert.IsType<DropColumnOperation>(operation).Column),
            operation => Assert.Equal(
                "IX_users_email",
                Assert.IsType<CreateIndexOperation>(operation).Index.Name));
    }

    [Fact]
    public void Alter_table_builds_named_check_constraint_operations()
    {
        var migration = new MigrationPlan();
        using (var table = migration.AlterTable("products"))
        {
            table.AddCheck("CK_products_price", "price >= 0");
            table.DropCheck("CK_products_legacy");
        }

        Assert.Collection(
            migration.Operations,
            operation => Assert.Equal(
                "price >= 0",
                Assert.IsType<AddCheckConstraintOperation>(operation).Check.Predicate),
            operation => Assert.Equal(
                "CK_products_legacy",
                Assert.IsType<DropCheckConstraintOperation>(operation).Check));
    }

    [Fact]
    public void Alter_table_reserves_its_group_order_and_requires_disposal()
    {
        var migration = new MigrationPlan();
        var table = migration.AlterTable("users");
        table.String("email");
        migration.ExecuteSql("select 1");
        table.DropColumn("obsolete");

        Assert.Throws<InvalidOperationException>(() => migration.Operations);

        table.Dispose();

        Assert.Collection(
            migration.Operations,
            operation => Assert.IsType<AddColumnOperation>(operation),
            operation => Assert.IsType<DropColumnOperation>(operation),
            operation => Assert.IsType<ExecuteSqlOperation>(operation));
        Assert.Throws<InvalidOperationException>(() => table.String("late"));
    }

    [Fact]
    public void Change_is_rejected_inside_create_table()
    {
        var migration = new MigrationPlan();
        using var table = migration.CreateTable("users");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.String("name").Change());

        Assert.Contains("AlterTable", exception.Message);
    }

    [Fact]
    public void Conditional_column_modifiers_are_one_operation_conditions()
    {
        var migration = new MigrationPlan();
        using (var table = migration.AlterTable("users"))
        {
            table.IfNotExists().String("email", 250).Nullable();
            table.String("name", 150);
            table.IfExists().DropColumn("obsolete");
            table.IfNotExists().CreateIndex("IX_users_email", "email");
            table.IfExists().DropIndex("IX_users_obsolete");
        }

        Assert.Collection(
            migration.Operations,
            operation => Assert.Equal(
                MigrationOperationCondition.IfNotExists,
                Assert.IsType<AddColumnOperation>(operation).Condition),
            operation => Assert.Equal(
                MigrationOperationCondition.None,
                Assert.IsType<AddColumnOperation>(operation).Condition),
            operation => Assert.Equal(
                MigrationOperationCondition.IfExists,
                Assert.IsType<DropColumnOperation>(operation).Condition),
            operation => Assert.Equal(
                MigrationOperationCondition.IfNotExists,
                Assert.IsType<CreateIndexOperation>(operation).Condition),
            operation => Assert.Equal(
                MigrationOperationCondition.IfExists,
                Assert.IsType<DropIndexOperation>(operation).Condition));
    }

    [Fact]
    public void Drop_table_if_exists_is_a_conditional_operation()
    {
        var migration = new MigrationPlan();

        migration.DropTableIfExists("users");

        var operation = Assert.IsType<DropTableOperation>(
            Assert.Single(migration.Operations));
        Assert.Equal(MigrationOperationCondition.IfExists, operation.Condition);
    }

    [Fact]
    public void Invalid_conditional_column_combinations_are_rejected()
    {
        var migration = new MigrationPlan();
        using var table = migration.AlterTable("users");

        Assert.Throws<InvalidOperationException>(() =>
            table.IfExists().String("email"));
        Assert.Throws<InvalidOperationException>(() =>
            table.IfNotExists().DropColumn("email"));
        Assert.Throws<InvalidOperationException>(() =>
            table.IfExists().CreateIndex("IX_users_email", "email"));
        Assert.Throws<InvalidOperationException>(() =>
            table.IfNotExists().DropIndex("IX_users_email"));
        Assert.Throws<InvalidOperationException>(() =>
            table.IfNotExists().String("email").Change());

        var oneShot = table.IfNotExists();
        oneShot.String("first");
        Assert.Throws<InvalidOperationException>(() => oneShot.String("second"));
    }

    [Fact]
    public void Invalid_definitions_are_rejected_before_compilation()
    {
        Assert.Throws<ArgumentException>(() =>
            new ColumnDefinition(" ", MigrationColumnType.String));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ColumnDefinition("name", MigrationColumnType.String, Length: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ColumnDefinition(
                "amount", MigrationColumnType.Decimal, Precision: 2, Scale: 3));
        Assert.Throws<ArgumentException>(() =>
            new IndexDefinition(null, ["name", "NAME"]));
        Assert.Throws<ArgumentException>(() =>
            new ForeignKeyDefinition(null, ["account_id"], "accounts", [" "]));
        var unnamedForeignKey = new MigrationPlan();
        Assert.Throws<ArgumentException>(() => unnamedForeignKey.AddForeignKey(
            "users",
            new ForeignKeyDefinition(null, ["account_id"], "accounts", ["id"])));

        var duplicateColumns = new MigrationPlan();
        using (var table = duplicateColumns.CreateTable("users"))
        {
            table.String("name");
            table.String("NAME");
        }
        Assert.Throws<InvalidOperationException>(() => duplicateColumns.Operations);

        var unknownIndexColumn = new MigrationPlan();
        using (var table = unknownIndexColumn.CreateTable("users"))
        {
            table.Identity();
            table.Index("IX_users_missing", "missing");
        }
        Assert.Throws<InvalidOperationException>(() => unknownIndexColumn.Operations);
    }

    [Fact]
    public void Open_table_scope_cannot_be_materialized_or_mutated_after_disposal()
    {
        var migration = new MigrationPlan();
        var table = migration.CreateTable("users");

        Assert.Throws<InvalidOperationException>(() => migration.Operations);

        table.Dispose();

        Assert.Throws<InvalidOperationException>(() => table.String("name"));
    }

    [Fact]
    public void Migration_id_defaults_to_type_name_and_can_be_overridden()
    {
        Assert.Equal(nameof(M2026_08_15_142530_CreateUsers),
            new M2026_08_15_142530_CreateUsers().Id);
        Assert.Equal("legacy-users", new RenamedMigration().Id);
        Assert.Throws<MigrationNotReversibleException>(() =>
            new M2026_08_15_142530_CreateUsers().Down(new MigrationPlan()));
    }

    private sealed class M2026_08_15_142530_CreateUsers : Migration
    {
        public override void Up(MigrationPlan migration)
        {
        }
    }

    private sealed class RenamedMigration : Migration
    {
        public override string Id => "legacy-users";

        public override void Up(MigrationPlan migration)
        {
        }
    }
}
