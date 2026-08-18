namespace SnapData.Migrations;

public sealed class MigrationRunnerOptions
{
    public string HistoryTable { get; init; } = "__snapdata_migrations";

    public MigrationLocking Locking { get; init; } = MigrationLocking.Required;

    public IMigrationLock? MigrationLock { get; init; }

    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan LockLeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    public string? LockResource { get; init; }

    public MigrationRollbackPolicy RollbackPolicy { get; init; } =
        MigrationRollbackPolicy.Disabled;
}

public enum MigrationRollbackPolicy
{
    Disabled,
    Enabled
}
