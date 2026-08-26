using System.Text;
using SnapData.Migrations;
using SnapData.Migrations.Cli.Runtime;

namespace SnapData.Migrations.Cli.Tests;

public sealed class MigrationScriptFileWriterTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"snapdata-script-{Guid.NewGuid():N}");

    [Fact]
    public void Writes_plain_utf8_script_with_migration_and_statement_boundaries()
    {
        Directory.CreateDirectory(directory);
        var outputFile = Path.Combine(directory, "deployment.sql");

        new MigrationScriptFileWriter().Write(
            outputFile,
            [
                Script(
                    "001_create_users",
                    "CREATE TABLE users (id INTEGER)",
                    "CREATE INDEX ix_users ON users(id);"),
                Script("002_seed", "INSERT INTO users VALUES (1)")
            ],
            overwrite: false);

        var bytes = File.ReadAllBytes(outputFile);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var sql = Encoding.UTF8.GetString(bytes);
        Assert.Contains("-- Migration: 001_create_users", sql);
        Assert.Contains("-- Statement: 1/2", sql);
        Assert.Contains("CREATE TABLE users (id INTEGER);", sql);
        Assert.DoesNotContain(";;", sql);
        Assert.Contains("-- Migration: 002_seed", sql);
    }

    [Fact]
    public void Refuses_to_replace_an_existing_file_without_force()
    {
        Directory.CreateDirectory(directory);
        var outputFile = Path.Combine(directory, "deployment.sql");
        File.WriteAllText(outputFile, "keep me");

        var exception = Assert.Throws<IOException>(() =>
            new MigrationScriptFileWriter().Write(
                outputFile,
                [Script("001", "SELECT 1")],
                overwrite: false));

        Assert.Contains("--force", exception.Message);
        Assert.Equal("keep me", File.ReadAllText(outputFile));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Force_replaces_an_existing_file()
    {
        Directory.CreateDirectory(directory);
        var outputFile = Path.Combine(directory, "deployment.sql");
        File.WriteAllText(outputFile, "old");

        new MigrationScriptFileWriter().Write(
            outputFile,
            [Script("001", "SELECT 1")],
            overwrite: true);

        Assert.Contains("SELECT 1;", File.ReadAllText(outputFile));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    private static MigrationScript Script(string id, params string[] statements) =>
        new(
            id,
            MigrationDirection.Up,
            statements.Select(sql => new MigrationStatement(sql)));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
