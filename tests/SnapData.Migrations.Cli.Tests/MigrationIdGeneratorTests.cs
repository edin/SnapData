using SnapData.Migrations.Cli.Scaffolding;

namespace SnapData.Migrations.Cli.Tests;

public sealed class MigrationIdGeneratorTests
{
    private static readonly DateTimeOffset Now = new(
        2026, 8, 23, 14, 5, 9, TimeSpan.FromHours(2));

    [Fact]
    public void Generates_a_sortable_utc_timestamp_and_normalized_slug()
    {
        var generator = new MigrationIdGenerator(() => Now);

        var id = generator.Generate(MigrationName.Parse("Create orders"), []);

        Assert.Equal("20260823120509_create_orders", id);
    }

    [Fact]
    public void Advances_beyond_the_newest_existing_timestamp()
    {
        var generator = new MigrationIdGenerator(() => Now);

        var id = generator.Generate(
            MigrationName.Parse("Create orders"),
            [
                "20260823120512_add_users",
                "001_legacy_migration",
                "not-a-timestamp"
            ]);

        Assert.Equal("20260823120513_create_orders", id);
    }

    [Fact]
    public void Avoids_a_case_insensitive_id_collision()
    {
        var generator = new MigrationIdGenerator(() => Now);

        var id = generator.Generate(
            MigrationName.Parse("Create orders"),
            ["20260823120509_CREATE_ORDERS"]);

        Assert.Equal("20260823120510_create_orders", id);
    }
}
