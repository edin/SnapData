using SnapData.Migrations.Cli.Configuration;

namespace SnapData.Migrations.Cli.Tests;

public sealed class IniConfigurationLoaderTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), $"snapdata-cli-{Guid.NewGuid():N}");

    public IniConfigurationLoaderTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Finds_snap_ini_upward_and_resolves_relative_paths()
    {
        File.WriteAllText(
            Path.Combine(directory, "snap.ini"),
            """
            [Migration]
            Provider=Sqlite
            Connection=Data Source=database/app.db
            Assembly=artifacts/App.Migrations.dll
            Project=src/App.csproj
            Configuration=Release
            TargetFramework=net8.0
            Bundle=App.Migrations.AppMigrationBundle
            MigrationsPath=database/migrations
            MigrationsNamespace=App.Migrations
            HistoryTable=app_migrations
            Timeout=45.5
            """);
        var child = Directory.CreateDirectory(
            Path.Combine(directory, "src", "nested")).FullName;

        var result = new IniConfigurationLoader().Load(child);

        Assert.Equal(Path.Combine(directory, "snap.ini"), result.ConfigFile);
        Assert.Equal("Sqlite", result.Provider);
        Assert.Equal("Data Source=database/app.db", result.Connection);
        Assert.Equal(
            Path.Combine(directory, "artifacts", "App.Migrations.dll"),
            result.AssemblyPath);
        Assert.Equal(Path.Combine(directory, "src", "App.csproj"), result.ProjectPath);
        Assert.Equal("Release", result.BuildConfiguration);
        Assert.Equal("net8.0", result.TargetFramework);
        Assert.Equal("App.Migrations.AppMigrationBundle", result.BundleType);
        Assert.Equal(
            Path.Combine(directory, "database", "migrations"),
            result.MigrationsPath);
        Assert.Equal("App.Migrations", result.MigrationsNamespace);
        Assert.Equal("app_migrations", result.HistoryTable);
        Assert.Equal(TimeSpan.FromSeconds(45.5), result.Timeout);
    }

    [Fact]
    public void Profile_overrides_base_values_and_supports_legacy_names()
    {
        File.WriteAllText(
            Path.Combine(directory, "snap.ini"),
            """
            [Migration]
            Dialect=Sqlite
            Connection=Data Source=app.db
            MigrationsTable=legacy_history
            Assembly=base.dll

            [Migration:production]
            Dialect=Postgres
            Connection=${env.SNAPDATA_TEST_CONNECTION}
            Assembly=production.dll
            """);
        var loader = new IniConfigurationLoader(name =>
            name == "SNAPDATA_TEST_CONNECTION" ? "Host=database" : null);

        var result = loader.Load(directory, "production");

        Assert.Equal("production", result.Profile);
        Assert.Equal("Postgres", result.Provider);
        Assert.Equal("Host=database", result.Connection);
        Assert.Equal(
            Path.Combine(directory, "production.dll"),
            result.AssemblyPath);
        Assert.Equal("legacy_history", result.HistoryTable);
        Assert.Equal("Debug", result.BuildConfiguration);
        Assert.Null(result.TargetFramework);
        Assert.Null(result.BundleType);
    }

    [Fact]
    public void Missing_profile_and_environment_variable_fail_clearly()
    {
        File.WriteAllText(
            Path.Combine(directory, "snap.ini"),
            """
            [Migration]
            Provider=Sqlite
            Connection=${env.MISSING_CONNECTION}
            """);
        var loader = new IniConfigurationLoader(_ => null);

        var missingProfile = Assert.Throws<InvalidOperationException>(() =>
            loader.Load(directory, "unknown"));
        Assert.Contains("profile 'unknown'", missingProfile.Message);

        var missingEnvironment = Assert.Throws<InvalidOperationException>(() =>
            loader.Load(directory));
        Assert.Contains("MISSING_CONNECTION", missingEnvironment.Message);
    }

    [Fact]
    public void Explicit_config_path_is_resolved_from_the_start_directory()
    {
        var configDirectory = Directory.CreateDirectory(
            Path.Combine(directory, "config")).FullName;
        File.WriteAllText(
            Path.Combine(configDirectory, "custom.ini"),
            """
            [Migration]
            Provider=Sqlite
            Connection=Data Source=:memory:
            """);

        var result = new IniConfigurationLoader().Load(
            directory, configFile: Path.Combine("config", "custom.ini"));

        Assert.Equal(
            Path.Combine(configDirectory, "custom.ini"),
            result.ConfigFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
