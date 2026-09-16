using System.Text;
using Microsoft.Data.Sqlite;
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
        Assert.Contains("CREATE TABLE users (id INTEGER)" + Environment.NewLine + ";", sql);
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

        Assert.Contains("SELECT 1" + Environment.NewLine + ";", File.ReadAllText(outputFile));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Theory]
    [InlineData("-- trailing comment")]
    [InlineData("-- trailing comment;")]
    [InlineData("; -- already terminated;")]
    [InlineData("/* block comment */")]
    public void Export_with_trailing_comments_executes_as_separate_statements(string suffix)
    {
        Directory.CreateDirectory(directory);
        var outputFile = Path.Combine(directory, "comments.sql");
        new MigrationScriptFileWriter().Write(outputFile,
            [Script("001", "CREATE TABLE items (name TEXT) " + suffix,
                "INSERT INTO items VALUES ('literal -- not comment;') " + suffix)], false);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(outputFile);
        command.ExecuteNonQuery();
        command.CommandText = "SELECT name FROM items";
        Assert.Equal("literal -- not comment;", command.ExecuteScalar());
    }

    [Fact]
    public void Empty_export_replaces_old_sql_with_explicit_no_op_and_history_warning()
    {
        Directory.CreateDirectory(directory);
        var outputFile = Path.Combine(directory, "empty.sql");
        File.WriteAllText(outputFile, "DELETE FROM important_data;");
        new MigrationScriptFileWriter().Write(outputFile, [], true);
        var sql = File.ReadAllText(outputFile);
        Assert.DoesNotContain("DELETE", sql);
        Assert.Contains("No pending migrations", sql);
        Assert.Contains("does not update SnapData migration history", sql);
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
