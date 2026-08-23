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
        if (configuration.ScriptsPath is not null)
        {
            if (configuration.ProjectPath is not null || configuration.AssemblyPath is not null)
            {
                throw new InvalidOperationException(
                    $"Migration Scripts cannot be combined with Project or Assembly in '{configuration.ConfigFile}'.");
            }

            var scripts = await new SqlMigrationFileCatalog().LoadAsync(
                configuration.ScriptsPath,
                cancellationToken).ConfigureAwait(false);
            return new MigrationSource(
                configuration,
                configuration.ScriptsPath,
                scripts,
                MigrationSourceKind.SqlScripts);
        }

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
                $"No migration source is configured in '{configuration.ConfigFile}'. " +
                "Configure Scripts, Project, or Assembly.");
        }

        var catalog = new MigrationAssemblyCatalog().Load(
            assemblyPath,
            configuration.MigrationsNamespace,
            configuration.BundleType);
        return new MigrationSource(configuration, assemblyPath, catalog);
    }
}
