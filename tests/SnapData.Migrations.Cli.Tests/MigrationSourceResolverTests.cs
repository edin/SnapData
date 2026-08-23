using SnapData.Migrations;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Runtime;

namespace SnapData.Migrations.Cli.Tests;

public sealed class MigrationSourceResolverTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"snapdata-source-{Guid.NewGuid():N}");

    public MigrationSourceResolverTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Resolves_sql_scripts_without_an_assembly_or_project()
    {
        File.WriteAllText(
            Path.Combine(directory, "001_users.sql"),
            "CREATE TABLE users (id INTEGER);");
        var configuration = Configuration() with { ScriptsPath = directory };

        var source = await new MigrationSourceResolver().ResolveAsync(configuration);

        Assert.Equal(MigrationSourceKind.SqlScripts, source.Kind);
        Assert.Equal(directory, source.SourcePath);
        Assert.Equal("001_users", Assert.Single(source.Catalog).Id);
        Assert.IsType<SqlMigration>(Assert.Single(source.Catalog.Migrations));
    }

    [Fact]
    public async Task Rejects_scripts_combined_with_a_compiled_source()
    {
        var configuration = Configuration() with
        {
            ScriptsPath = directory,
            ProjectPath = Path.Combine(directory, "App.csproj")
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MigrationSourceResolver().ResolveAsync(configuration));

        Assert.Contains("cannot be combined", exception.Message);
    }

    [Fact]
    public async Task Missing_source_lists_all_supported_configuration_modes()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MigrationSourceResolver().ResolveAsync(Configuration()));

        Assert.Contains("Scripts, Project, or Assembly", exception.Message);
    }

    private MigrationCliConfiguration Configuration() => new(
        Path.Combine(directory, "snap.ini"),
        null,
        "Sqlite",
        "Data Source=app.db",
        null,
        null,
        "Debug",
        null,
        null,
        null,
        null,
        "__snapdata_migrations",
        MigrationLocking.Required,
        TimeSpan.FromSeconds(30));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
