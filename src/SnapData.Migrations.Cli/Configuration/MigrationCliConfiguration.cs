namespace SnapData.Migrations.Cli.Configuration;

internal sealed record MigrationCliConfiguration(
    string ConfigFile,
    string? Profile,
    string Provider,
    string Connection,
    string? AssemblyPath,
    string? ProjectPath,
    string BuildConfiguration,
    string? TargetFramework,
    string? BundleType,
    string? MigrationsPath,
    string? MigrationsNamespace,
    string HistoryTable,
    TimeSpan Timeout);
