using SnapData.Migrations;

namespace SnapData.Migrations.Cli.Runtime;

internal sealed record MigrationPreflightResult(
    IReadOnlyList<MigrationStatusEntry> Status,
    IReadOnlyList<Migration> PendingMigrations,
    IReadOnlyList<MigrationStatusEntry> InvalidEntries)
{
    public bool IsValid => InvalidEntries.Count == 0;
}

internal static class MigrationPreflight
{
    public static async Task<MigrationPreflightResult> EvaluateAsync(
        MigrationRunner runner,
        MigrationSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(source);
        var status = await runner.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var pendingIds = status
            .Where(entry => entry.State == MigrationStatusState.Pending)
            .Select(entry => entry.MigrationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = source.Catalog.Migrations
            .Where(migration => pendingIds.Contains(migration.Id))
            .ToArray();
        var invalid = status.Where(entry => entry.State is
            MigrationStatusState.Changed or
            MigrationStatusState.Missing or
            MigrationStatusState.OutOfOrder).ToArray();
        return new MigrationPreflightResult(status, pending, invalid);
    }

    public static IReadOnlyList<Migration> SelectRollbackMigrations(
        IReadOnlyList<Migration> migrations,
        IReadOnlyList<MigrationStatusEntry> status,
        int steps)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        var appliedIds = status
            .Where(entry => entry.State is
                MigrationStatusState.Applied or
                MigrationStatusState.Unverifiable)
            .Select(entry => entry.MigrationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return migrations
            .Where(migration => appliedIds.Contains(migration.Id))
            .Reverse()
            .Take(steps)
            .ToArray();
    }
}
