namespace SnapData.Migrations;

public interface IMigrationCompiler
{
    MigrationScript Compile(
        string migrationId,
        MigrationDirection direction,
        MigrationPlan plan);
}
