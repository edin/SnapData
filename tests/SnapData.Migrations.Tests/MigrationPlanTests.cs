using SnapData.Migrations;
using SnapData.Schema;

namespace SnapData.Migrations.Tests;

public sealed class MigrationPlanTests
{
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
            table.Index("IX_users_name", "name");
            table.ForeignKey(
                "FK_users_account",
                ["account_id"],
                "accounts",
                ["id"],
                onDelete: ReferentialAction.Cascade);
        }

        var operation = Assert.IsType<CreateTableOperation>(
            Assert.Single(migration.Operations));
        Assert.Equal("users", operation.Table);
        Assert.Equal(6, operation.Columns.Count);
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
