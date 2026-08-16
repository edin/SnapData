using SnapData.Migrations;

namespace SnapData.Migrations.Tests;

public sealed class MigrationBundleTests
{
    [Fact]
    public void Bundle_preserves_explicit_registration_order()
    {
        var bundle = new AppMigrations();

        Assert.Equal(
            ["002-create-users", "001-bootstrap", "003-create-orders"],
            bundle.Select(migration => migration.Id));
    }

    [Fact]
    public void Collection_accepts_types_instances_and_ranges()
    {
        var migrations = new MigrationCollection()
            .Add<BootstrapMigration>()
            .Add(new CreateUsersMigration())
            .AddRange([new CreateOrdersMigration()]);

        Assert.Equal(3, migrations.Count);
        Assert.IsType<BootstrapMigration>(migrations[0]);
        Assert.IsType<CreateUsersMigration>(migrations[1]);
        Assert.IsType<CreateOrdersMigration>(migrations[2]);
    }

    [Fact]
    public void Duplicate_ids_are_rejected_case_insensitively()
    {
        var migrations = new MigrationCollection().Add(new NamedMigration("create-users"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            migrations.Add(new NamedMigration("CREATE-USERS")));

        Assert.Contains("already registered", exception.Message);
    }

    [Fact]
    public void Bundle_is_configured_once_and_exposes_a_stable_snapshot()
    {
        var bundle = new CountingBundle();

        _ = bundle.Count;
        _ = bundle.ToArray();

        Assert.Equal(1, bundle.ConfigureCalls);
    }

    private sealed class AppMigrations : MigrationBundle
    {
        protected override void Configure(MigrationCollection migrations)
        {
            migrations
                .Add<CreateUsersMigration>()
                .Add<BootstrapMigration>()
                .Add<CreateOrdersMigration>();
        }
    }

    private sealed class CountingBundle : MigrationBundle
    {
        public int ConfigureCalls { get; private set; }

        protected override void Configure(MigrationCollection migrations)
        {
            ConfigureCalls++;
            migrations.Add<BootstrapMigration>();
        }
    }

    public sealed class BootstrapMigration : Migration
    {
        public override string Id => "001-bootstrap";
    }

    public sealed class CreateUsersMigration : Migration
    {
        public override string Id => "002-create-users";
    }

    public sealed class CreateOrdersMigration : Migration
    {
        public override string Id => "003-create-orders";
    }

    private sealed class NamedMigration(string id) : Migration
    {
        public override string Id { get; } = id;
    }
}
