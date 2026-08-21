using SnapData.Migrations.Cli.Scaffolding;

namespace SnapData.Migrations.Cli.Tests;

public sealed class MigrationScaffolderTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"snapdata-init-{Guid.NewGuid():N}");

    [Fact]
    public void Creates_portable_configuration_project_and_sample_migrations()
    {
        var result = new MigrationScaffolder().Create(directory);

        Assert.True(File.Exists(result.ConfigFile));
        Assert.True(File.Exists(result.ProjectFile));
        Assert.Equal(2, result.MigrationFiles.Count);
        Assert.All(result.MigrationFiles, path => Assert.True(File.Exists(path)));
        Assert.Contains("Provider=Sqlite", File.ReadAllText(result.ConfigFile));
        Assert.Contains(
            "PackageReference Include=\"SnapData.Migrations\"",
            File.ReadAllText(result.ProjectFile));
        Assert.True(File.Exists(Path.Combine(directory, "Migrations", ".gitignore")));
    }

    [Fact]
    public void Refuses_to_overwrite_an_existing_workspace()
    {
        var scaffolder = new MigrationScaffolder();
        var result = scaffolder.Create(directory);
        var original = File.ReadAllText(result.ConfigFile);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            scaffolder.Create(directory));

        Assert.Contains("No files were changed", exception.Message);
        Assert.Equal(original, File.ReadAllText(result.ConfigFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
