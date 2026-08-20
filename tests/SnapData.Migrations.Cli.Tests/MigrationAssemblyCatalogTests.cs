using SnapData.Migrations.Cli.Discovery;

namespace SnapData.Migrations.Cli.Tests
{
    public sealed class MigrationAssemblyCatalogTests
    {
        private static readonly string AssemblyPath =
            typeof(MigrationAssemblyCatalogTests).Assembly.Location;

        [Fact]
        public void Loads_filters_and_orders_migrations()
        {
            var migrations = new MigrationAssemblyCatalog().Load(
                AssemblyPath,
                "SnapData.Migrations.Cli.Tests.Fixtures.Valid");

            Assert.Equal(
                ["001-create-users", "002-create-orders"],
                migrations.Select(migration => migration.Id));
            Assert.All(migrations, migration =>
                Assert.StartsWith(
                    "SnapData.Migrations.Cli.Tests.Fixtures.Valid.",
                    migration.TypeName));
        }

        [Fact]
        public void Duplicate_migration_ids_fail_clearly()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new MigrationAssemblyCatalog().Load(
                    AssemblyPath,
                    "SnapData.Migrations.Cli.Tests.Fixtures.Duplicates"));

            Assert.Contains("already registered", exception.Message);
        }

        [Fact]
        public void Missing_assembly_fails_clearly()
        {
            var missing = Path.Combine(
                Path.GetDirectoryName(AssemblyPath)!,
                "missing-migrations.dll");

            var exception = Assert.Throws<FileNotFoundException>(() =>
                new MigrationAssemblyCatalog().Load(missing));

            Assert.Contains("does not exist", exception.Message);
        }

        [Fact]
        public void Uses_single_bundle_and_preserves_its_registration_order()
        {
            var migrations = new MigrationAssemblyCatalog().Load(
                AssemblyPath,
                "SnapData.Migrations.Cli.Tests.Fixtures.Bundle");

            Assert.Equal(
                ["002-bundle-second", "001-bundle-first"],
                migrations.Select(migration => migration.Id));
        }

        [Fact]
        public void Multiple_bundles_require_an_explicit_selection()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new MigrationAssemblyCatalog().Load(
                    AssemblyPath,
                    "SnapData.Migrations.Cli.Tests.Fixtures.MultipleBundles"));
            Assert.Contains("Multiple migration bundles", exception.Message);

            var migrations = new MigrationAssemblyCatalog().Load(
                AssemblyPath,
                "SnapData.Migrations.Cli.Tests.Fixtures.MultipleBundles",
                "SnapData.Migrations.Cli.Tests.Fixtures.MultipleBundles.SecondBundle");
            Assert.Equal("second-bundle", Assert.Single(migrations).Id);
        }
    }
}

namespace SnapData.Migrations.Cli.Tests.Fixtures.Valid
{
    public sealed class CreateOrders : global::SnapData.Migrations.Migration
    {
        public override string Id => "002-create-orders";
    }

    public sealed class CreateUsers : global::SnapData.Migrations.Migration
    {
        public override string Id => "001-create-users";
    }
}

namespace SnapData.Migrations.Cli.Tests.Fixtures.Valid.Nested
{
    public abstract class IgnoredMigration : global::SnapData.Migrations.Migration;
}

namespace SnapData.Migrations.Cli.Tests.Fixtures.Duplicates
{
    public sealed class FirstMigration : global::SnapData.Migrations.Migration
    {
        public override string Id => "duplicate";
    }

    public sealed class SecondMigration : global::SnapData.Migrations.Migration
    {
        public override string Id => "duplicate";
    }
}

namespace SnapData.Migrations.Cli.Tests.Fixtures.Bundle
{
    public sealed class AppBundle : global::SnapData.Migrations.MigrationBundle
    {
        protected override void Configure(
            global::SnapData.Migrations.MigrationCollection migrations)
        {
            migrations.Add<SecondMigration>().Add<FirstMigration>();
        }
    }

    public sealed class FirstMigration : global::SnapData.Migrations.Migration
    {
        public override string Id => "001-bundle-first";
    }

    public sealed class SecondMigration : global::SnapData.Migrations.Migration
    {
        public override string Id => "002-bundle-second";
    }
}

namespace SnapData.Migrations.Cli.Tests.Fixtures.MultipleBundles
{
    public sealed class FirstBundle : global::SnapData.Migrations.MigrationBundle
    {
        protected override void Configure(
            global::SnapData.Migrations.MigrationCollection migrations) =>
            migrations.Add(new FirstMigration());
    }

    public sealed class SecondBundle : global::SnapData.Migrations.MigrationBundle
    {
        protected override void Configure(
            global::SnapData.Migrations.MigrationCollection migrations) =>
            migrations.Add(new SecondMigration());
    }

    public sealed class FirstMigration : global::SnapData.Migrations.Migration
    {
        public override string Id => "first-bundle";
    }

    public sealed class SecondMigration : global::SnapData.Migrations.Migration
    {
        public override string Id => "second-bundle";
    }
}
