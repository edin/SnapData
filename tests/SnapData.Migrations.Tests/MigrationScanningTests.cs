using SnapData.Migrations;

namespace SnapData.Migrations.Tests;

public sealed class MigrationScanningTests
{
    [Fact]
    public void Assembly_scan_discovers_and_sorts_concrete_migrations_by_id()
    {
        var migrations = new MigrationCollection()
            .ScanAssembly(
                typeof(MigrationScanningTests).Assembly,
                type => type.DeclaringType == typeof(ScanFixtures));

        Assert.Equal(
            [
                "M2026_08_15_100000_CreateUsers",
                "M2026_08_15_110000_CreateOrders"
            ],
            migrations.Select(migration => migration.Id));
    }

    [Fact]
    public void Scanned_migrations_are_appended_after_explicit_entries()
    {
        var migrations = new MigrationCollection()
            .Add(new SqlMigration("000-bootstrap", "select 1"))
            .ScanTypes(
                [typeof(ScanFixtures.CreateOrders), typeof(ScanFixtures.CreateUsers)]);

        Assert.Equal(
            [
                "000-bootstrap",
                "M2026_08_15_100000_CreateUsers",
                "M2026_08_15_110000_CreateOrders"
            ],
            migrations.Select(migration => migration.Id));
    }

    [Fact]
    public void Abstract_and_open_generic_migrations_are_ignored()
    {
        var migrations = new MigrationCollection().ScanTypes(
            [typeof(AbstractMigration), typeof(GenericMigration<>)]);

        Assert.Empty(migrations);
    }

    [Fact]
    public void Migration_without_public_parameterless_constructor_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new MigrationCollection().ScanTypes([typeof(ConstructorMigration)]));

        Assert.Contains("public parameterless constructor", exception.Message);
    }

    public static class ScanFixtures
    {
        public sealed class CreateOrders : Migration
        {
            public override string Id => "M2026_08_15_110000_CreateOrders";
        }

        public sealed class CreateUsers : Migration
        {
            public override string Id => "M2026_08_15_100000_CreateUsers";
        }

        public abstract class Ignored : Migration;
    }

    public abstract class AbstractMigration : Migration;

    public sealed class GenericMigration<T> : Migration;

    public sealed class ConstructorMigration(string value) : Migration
    {
        public string Value { get; } = value;
    }
}
