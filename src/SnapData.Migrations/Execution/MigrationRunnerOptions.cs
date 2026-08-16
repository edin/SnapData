namespace SnapData.Migrations;

public sealed class MigrationRunnerOptions
{
    public string HistoryTable { get; init; } = "__snapdata_migrations";

    public MigrationLocking Locking { get; init; } = MigrationLocking.Required;

    public IMigrationLock? MigrationLock { get; init; }

    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public string? LockResource { get; init; }
}
