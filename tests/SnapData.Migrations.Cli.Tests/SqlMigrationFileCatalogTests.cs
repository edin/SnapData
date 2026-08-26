using SnapData.Migrations;
using SnapData.Migrations.Cli.Discovery;

namespace SnapData.Migrations.Cli.Tests;

public sealed class SqlMigrationFileCatalogTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"snapdata-sql-migrations-{Guid.NewGuid():N}");

    public SqlMigrationFileCatalogTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Loads_ordered_up_migrations_and_optional_paired_down_files()
    {
        Write("002_add_email.sql", "ALTER TABLE users ADD email TEXT;");
        Write("001_create_users.sql", "CREATE TABLE users (id INTEGER);");
        Write("001_create_users.down.sql", "DROP TABLE users;");
        Write("notes.txt", "ignored");

        var catalog = await new SqlMigrationFileCatalog().LoadAsync(directory);

        Assert.Equal(["001_create_users", "002_add_email"], catalog.Select(item => item.Id));
        var first = Assert.IsType<SqlMigration>(catalog.Migrations[0]);
        Assert.Equal("CREATE TABLE users (id INTEGER);", Assert.Single(first.UpStatements));
        Assert.Equal("DROP TABLE users;", Assert.Single(first.DownStatements!));
        Assert.Null(Assert.IsType<SqlMigration>(catalog.Migrations[1]).DownStatements);
    }

    [Fact]
    public async Task Loads_up_and_down_sections_from_one_file()
    {
        Write(
            "001_create_users.sql",
            """
            CREATE TABLE users (id INTEGER);

              -- SnapData:Down

            DROP TABLE users;
            """);

        var catalog = await new SqlMigrationFileCatalog().LoadAsync(directory);
        var migration = Assert.IsType<SqlMigration>(Assert.Single(catalog.Migrations));

        Assert.Equal("CREATE TABLE users (id INTEGER);", Assert.Single(migration.UpStatements));
        Assert.Equal("DROP TABLE users;", Assert.Single(migration.DownStatements!));
    }

    [Fact]
    public async Task Loads_combined_sections_with_crlf_line_endings()
    {
        Write(
            "001_create_users.sql",
            "CREATE TABLE users (id INTEGER);\r\n\r\n" +
            "  -- SnapData:Down\r\n\r\n" +
            "DROP TABLE users;\r\n");

        var catalog = await new SqlMigrationFileCatalog().LoadAsync(directory);
        var migration = Assert.IsType<SqlMigration>(Assert.Single(catalog.Migrations));

        Assert.Equal("CREATE TABLE users (id INTEGER);", Assert.Single(migration.UpStatements));
        Assert.Equal("DROP TABLE users;", Assert.Single(migration.DownStatements!));
    }

    [Fact]
    public async Task Rejects_inline_and_paired_down_sql_for_the_same_migration()
    {
        Write(
            "001_users.sql",
            "CREATE TABLE users (id INTEGER);\n-- snapdata:down\nDROP TABLE users;");
        Write("001_users.down.sql", "DROP TABLE users;");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlMigrationFileCatalog().LoadAsync(directory));

        Assert.Contains("both inline", exception.Message);
    }

    [Fact]
    public async Task Rejects_an_orphaned_down_file()
    {
        Write("001_users.down.sql", "DROP TABLE users;");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlMigrationFileCatalog().LoadAsync(directory));

        Assert.Contains("no matching Up", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-- snapdata:down\nDROP TABLE users;")]
    [InlineData("CREATE TABLE users (id INTEGER);\n-- snapdata:down")]
    public async Task Rejects_empty_required_sql_sections(string content)
    {
        Write("001_users.sql", content);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlMigrationFileCatalog().LoadAsync(directory));

        Assert.Contains("empty", exception.Message);
    }

    [Fact]
    public async Task Rejects_multiple_inline_down_markers()
    {
        Write(
            "001_users.sql",
            "SELECT 1;\n-- snapdata:down\nSELECT 2;\n-- snapdata:down\nSELECT 3;");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlMigrationFileCatalog().LoadAsync(directory));

        Assert.Contains("more than one", exception.Message);
    }

    [Fact]
    public async Task Rejects_a_missing_scripts_directory()
    {
        Directory.Delete(directory);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new SqlMigrationFileCatalog().LoadAsync(directory));
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(directory, name), content);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
