using SnapData.Migrations;
using SnapData.Migrations.Cli.Runtime;

namespace SnapData.Migrations.Cli.Tests;

public sealed class MigrationPreflightTests
{
    [Fact]
    public void Selects_applied_migrations_in_reverse_bundle_order()
    {
        Migration[] migrations =
        [
            new NamedMigration("001-first"),
            new NamedMigration("002-second"),
            new NamedMigration("003-third")
        ];
        MigrationStatusEntry[] status =
        [
            new("001-first", MigrationStatusState.Applied, BundleOrder: 1),
            new("002-second", MigrationStatusState.Unverifiable, BundleOrder: 2),
            new("003-third", MigrationStatusState.Pending, BundleOrder: 3)
        ];

        var selected = MigrationPreflight.SelectRollbackMigrations(
            migrations,
            status,
            steps: 5);

        Assert.Equal(["002-second", "001-first"], selected.Select(item => item.Id));
    }

    [Fact]
    public void Rejects_non_positive_rollback_steps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MigrationPreflight.SelectRollbackMigrations([], [], 0));
    }

    private sealed class NamedMigration(string id) : Migration
    {
        public override string Id { get; } = id;
    }
}
