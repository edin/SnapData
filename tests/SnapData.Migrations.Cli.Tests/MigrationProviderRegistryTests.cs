using Microsoft.Data.Sqlite;
using SnapData.Migrations;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Discovery;
using SnapData.Migrations.Cli.Runtime;

namespace SnapData.Migrations.Cli.Tests;

public sealed class MigrationProviderRegistryTests : IDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"snapdata-status-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData("SQLite", Provider.Sqlite)]
    [InlineData("mssql", Provider.SqlServer)]
    [InlineData("PostgreSQL", Provider.Postgres)]
    [InlineData("MySql", Provider.MySql)]
    [InlineData("Firebird", Provider.Firebird)]
    public void Resolves_supported_provider_names(string requested, string expected)
    {
        Assert.Equal(expected, MigrationProviderRegistry.Resolve(requested).Name);
    }

    [Fact]
    public void Unknown_provider_fails_with_supported_names()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MigrationProviderRegistry.Resolve("oracle"));

        Assert.Contains("not supported", exception.Message);
        Assert.Contains("Sqlite", exception.Message);
    }

    [Fact]
    public async Task Sqlite_status_pipeline_is_read_only()
    {
        var configuration = new MigrationCliConfiguration(
            Path.Combine(Path.GetTempPath(), "snap.ini"),
            null,
            "Sqlite",
            $"Data Source={databasePath};Pooling=False",
            "App.Migrations.dll",
            null,
            "Debug",
            null,
            null,
            null,
            null,
            "__snapdata_migrations",
            MigrationLocking.Required,
            TimeSpan.FromSeconds(30));
        var catalog = new MigrationCatalog(
            new Migration[] { new PendingMigration() },
            bundleType: null);
        var source = new MigrationSource(
            configuration,
            configuration.AssemblyPath!,
            catalog);

        var runner = MigrationProviderRegistry.CreateRunner(source);
        var preflight = await MigrationPreflight.EvaluateAsync(runner, source);

        Assert.True(preflight.IsValid);
        Assert.Equal(
            MigrationStatusState.Pending,
            Assert.Single(preflight.Status).State);
        Assert.Same(
            catalog.Migrations[0],
            Assert.Single(preflight.PendingMigrations));
        await using var connection = new SqliteConnection(configuration.Connection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_schema WHERE name IN " +
            "('__snapdata_migrations', '__snapdata_migrations_lock')";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    public void Dispose()
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    private sealed class PendingMigration : Migration
    {
        public override string Id => "001-pending";
    }
}
