using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Discovery;

namespace SnapData.Migrations.Cli.Runtime;

internal enum MigrationSourceKind
{
    Assembly,
    SqlScripts
}

internal sealed record MigrationSource(
    MigrationCliConfiguration Configuration,
    string SourcePath,
    MigrationCatalog Catalog,
    MigrationSourceKind Kind = MigrationSourceKind.Assembly);
