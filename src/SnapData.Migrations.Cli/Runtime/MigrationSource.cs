using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Discovery;

namespace SnapData.Migrations.Cli.Runtime;

internal sealed record MigrationSource(
    MigrationCliConfiguration Configuration,
    string AssemblyPath,
    MigrationCatalog Catalog);
