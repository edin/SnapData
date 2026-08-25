using SnapData.Migrations.Cli.Scaffolding;

namespace SnapData.Migrations.Cli.Tests;

public sealed class SqlMigrationScaffolderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(
        2026, 8, 26, 14, 30, 15, TimeSpan.Zero);
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"snapdata-add-sql-{Guid.NewGuid():N}");

    public SqlMigrationScaffolderTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Creates_timestamped_paired_up_and_down_files_by_default()
    {
        var result = Scaffolder().Create(directory, "Create orders");

        Assert.Equal("20260826143015_create_orders", result.Id);
        Assert.False(result.Combined);
        Assert.NotNull(result.DownFile);
        Assert.True(File.Exists(result.UpFile));
        Assert.True(File.Exists(result.DownFile));
        Assert.Contains("CreateOrders (Up)", File.ReadAllText(result.UpFile));
        Assert.Contains("CreateOrders (Down)", File.ReadAllText(result.DownFile));
        Assert.DoesNotContain("snapdata:down", File.ReadAllText(result.UpFile));
    }

    [Fact]
    public void Creates_one_file_with_the_down_marker_in_combined_mode()
    {
        var result = Scaffolder().Create(
            directory,
            "CreateOrders",
            combined: true);

        Assert.True(result.Combined);
        Assert.Null(result.DownFile);
        var content = File.ReadAllText(result.UpFile);
        Assert.Contains("Write the Up SQL", content);
        Assert.Contains("-- snapdata:down", content);
        Assert.Contains("Write the Down SQL", content);
    }

    [Fact]
    public void Advances_past_existing_timestamped_migrations()
    {
        File.WriteAllText(
            Path.Combine(directory, "20260826143020_existing.sql"),
            "SELECT 1;");

        var result = Scaffolder().Create(directory, "Create orders");

        Assert.Equal("20260826143021_create_orders", result.Id);
    }

    [Fact]
    public void Existing_target_prevents_any_new_file_from_being_created()
    {
        var downFile = Path.Combine(
            directory,
            "20260826143015_create_orders.down.sql");
        File.WriteAllText(downFile, "existing");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Scaffolder().Create(directory, "Create orders"));

        Assert.Contains("No files were changed", exception.Message);
        Assert.False(File.Exists(Path.Combine(
            directory,
            "20260826143015_create_orders.sql")));
        Assert.Equal("existing", File.ReadAllText(downFile));
    }

    [Fact]
    public void Missing_scripts_directory_fails_clearly()
    {
        Directory.Delete(directory);

        Assert.Throws<DirectoryNotFoundException>(() =>
            Scaffolder().Create(directory, "Create orders"));
    }

    private static SqlMigrationScaffolder Scaffolder() => new(
        new MigrationIdGenerator(() => Now));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
