using SnapData.Schema;

namespace SnapData.Migrations;

public sealed class MigrationContext
{
    public MigrationContext(
        MigrationPlan plan,
        ISchemaInspector schemaInspector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(schemaInspector);
        Plan = plan;
        Schema = new MigrationSchema(schemaInspector, cancellationToken);
        CancellationToken = cancellationToken;
    }

    public MigrationPlan Plan { get; }

    public MigrationSchema Schema { get; }

    public CancellationToken CancellationToken { get; }
}
