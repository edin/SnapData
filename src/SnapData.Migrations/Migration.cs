namespace SnapData.Migrations;

public abstract class Migration
{
    public virtual string Id => GetType().Name;

    public virtual void Up(MigrationPlan migration)
    {
    }

    public virtual ValueTask UpAsync(MigrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Up(context.Plan);
        return ValueTask.CompletedTask;
    }

    public virtual void Down(MigrationPlan migration) =>
        throw new MigrationNotReversibleException(Id);

    public virtual ValueTask DownAsync(MigrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Down(context.Plan);
        return ValueTask.CompletedTask;
    }
}

public sealed class MigrationNotReversibleException(string migrationId)
    : InvalidOperationException($"Migration '{migrationId}' does not define a down plan.")
{
    public string MigrationId { get; } = migrationId;
}
