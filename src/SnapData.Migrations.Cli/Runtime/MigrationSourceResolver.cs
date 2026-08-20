using SnapData.Migrations.Cli.Build;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Discovery;

namespace SnapData.Migrations.Cli.Runtime;

internal sealed class MigrationSourceResolver
{
    public async Task<MigrationSource> ResolveAsync(
        MigrationCliConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var assemblyPath = configuration.ProjectPath is null
            ? configuration.AssemblyPath
            : await new MigrationProjectBuilder().BuildAsync(
                configuration.ProjectPath,
                configuration.BuildConfiguration,
                configuration.TargetFramework,
                cancellationToken).ConfigureAwait(false);
        if (assemblyPath is null)
        {
            throw new InvalidOperationException(
                $"Neither migration Project nor Assembly is configured in '{configuration.ConfigFile}'.");
        }

        var catalog = new MigrationAssemblyCatalog().Load(
            assemblyPath,
            configuration.MigrationsNamespace,
            configuration.BundleType);
        return new MigrationSource(configuration, assemblyPath, catalog);
    }
}
