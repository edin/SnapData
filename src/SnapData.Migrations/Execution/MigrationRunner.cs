namespace SnapData.Migrations;

public sealed class MigrationRunner
{
    private readonly SnapDatabase database;
    private readonly IReadOnlyList<Migration> migrations;
    private readonly IMigrationDialect dialect;
    private readonly MigrationHistoryRepository history;
    private readonly MigrationLocking locking;
    private readonly IMigrationLock? migrationLock;
    private readonly TimeSpan lockTimeout;
    private readonly TimeSpan lockLeaseDuration;
    private readonly string lockResource;
    private readonly string historyTable;
    private readonly MigrationRollbackPolicy rollbackPolicy;

    public MigrationRunner(
        SnapDatabase database,
        IReadOnlyList<Migration> migrations,
        IMigrationDialect dialect,
        MigrationRunnerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentNullException.ThrowIfNull(dialect);
        options ??= new MigrationRunnerOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HistoryTable);
        if (!Enum.IsDefined(options.Locking))
        {
            throw new ArgumentOutOfRangeException(nameof(options.Locking));
        }
        if (options.LockTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.LockTimeout));
        }
        if (options.LockLeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.LockLeaseDuration));
        }
        if (!Enum.IsDefined(options.RollbackPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options.RollbackPolicy));
        }

        this.database = database;
        this.migrations = SnapshotAndValidate(migrations);
        this.dialect = dialect;
        locking = options.Locking;
        migrationLock = options.MigrationLock ?? dialect.MigrationLock;
        lockTimeout = options.LockTimeout;
        lockLeaseDuration = options.LockLeaseDuration;
        historyTable = options.HistoryTable;
        lockResource = string.IsNullOrWhiteSpace(options.LockResource)
            ? $"SnapData.Migrations:{options.HistoryTable}"
            : options.LockResource;
        rollbackPolicy = options.RollbackPolicy;
        history = new MigrationHistoryRepository(options.HistoryTable, dialect);
    }

    public async Task<MigrationScript> PreviewAsync(
        Migration migration,
        MigrationDirection direction = MigrationDirection.Up,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migration);
        await using var session = await database.OpenSessionAsync(cancellationToken);
        return await PlanAsync(migration, direction, session, cancellationToken);
    }

    public async Task<IReadOnlyList<MigrationScript>> PreviewAsync(
        IEnumerable<Migration> migrationBundle,
        MigrationDirection direction = MigrationDirection.Up,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrationBundle);
        await using var session = await database.OpenSessionAsync(cancellationToken);
        var scripts = new List<MigrationScript>();
        foreach (var migration in migrationBundle)
        {
            scripts.Add(await PlanAsync(migration, direction, session, cancellationToken));
        }
        return scripts.AsReadOnly();
    }

    public Task<IReadOnlyList<MigrationScript>> PreviewAsync(
        MigrationDirection direction = MigrationDirection.Up,
        CancellationToken cancellationToken = default) =>
        PreviewAsync(migrations, direction, cancellationToken);

    public async Task<IReadOnlyList<MigrationHistoryEntry>> GetHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var session = await database.OpenSessionAsync(cancellationToken);
        await history.EnsureCreatedAsync(session, cancellationToken);
        return await history.ReadAsync(session, cancellationToken);
    }

    public async Task<IReadOnlyList<MigrationStatusEntry>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await using var session = await database.OpenSessionAsync(cancellationToken);
        var applied = await history.ExistsAsync(session, cancellationToken)
            ? await history.ReadAsync(session, cancellationToken)
            : Array.Empty<MigrationHistoryEntry>();
        return await BuildStatusAsync(session, applied, cancellationToken);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
        => await MigrateThroughAsync(migrations.Count, cancellationToken);

    public async Task MigrateToAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        var targetIndex = migrations
            .Select((migration, index) => (migration, index))
            .FirstOrDefault(item => string.Equals(
                item.migration.Id, migrationId, StringComparison.OrdinalIgnoreCase));
        if (targetIndex.migration is null)
        {
            throw new ArgumentException(
                $"Migration ID '{migrationId}' is not registered.", nameof(migrationId));
        }
        await MigrateThroughAsync(targetIndex.index + 1, cancellationToken);
    }

    private async Task MigrateThroughAsync(
        int migrationCount,
        CancellationToken cancellationToken)
    {
        await using var lockHandle = await AcquireLockAsync(cancellationToken);
        ThrowIfLockLost(lockHandle);
        await using var session = await database.OpenSessionAsync(cancellationToken);
        await history.EnsureCreatedAsync(session, cancellationToken);
        var historyEntries = await history.ReadAsync(session, cancellationToken);
        var status = await BuildStatusAsync(session, historyEntries, cancellationToken);
        ThrowIfInvalid(status);
        var applied = historyEntries.Select(item => item.MigrationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var beyondTarget = historyEntries.Where(entry =>
            migrations.Take(migrationCount).All(migration =>
                !string.Equals(migration.Id, entry.MigrationId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (beyondTarget.Length > 0)
        {
            throw new InvalidOperationException(
                "The database is already beyond the requested migration target. " +
                "Use explicit development rollback to move backward.");
        }

        foreach (var migration in migrations.Take(migrationCount)
            .Where(item => !applied.Contains(item.Id)))
        {
            await RenewLockAsync(lockHandle, cancellationToken);
            await using var transaction = await session.BeginTransactionAsync(
                cancellationToken: cancellationToken);
            var planned = await PlanDetailsAsync(
                migration, MigrationDirection.Up, transaction, cancellationToken);
            await ExecuteAsync(transaction, planned, cancellationToken);
            var appliedOrder = await history.GetNextAppliedOrderAsync(
                transaction, cancellationToken);
            await history.InsertAsync(
                transaction,
                migration.Id,
                appliedOrder,
                DateTimeOffset.UtcNow,
                planned.Script.Fingerprint,
                cancellationToken);
            ThrowIfLockLost(lockHandle);
            await transaction.CommitAsync(cancellationToken);
            await RenewLockAsync(lockHandle, cancellationToken);
        }
    }

    public async Task RollbackAsync(
        int steps = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        if (rollbackPolicy != MigrationRollbackPolicy.Enabled)
        {
            throw new InvalidOperationException(
                "Migration rollback is disabled. Enable it explicitly in MigrationRunnerOptions.");
        }
        await using var lockHandle = await AcquireLockAsync(cancellationToken);
        ThrowIfLockLost(lockHandle);
        await using var session = await database.OpenSessionAsync(cancellationToken);
        await history.EnsureCreatedAsync(session, cancellationToken);
        var historyEntries = await history.ReadAsync(session, cancellationToken);
        var status = await BuildStatusAsync(session, historyEntries, cancellationToken);
        ThrowIfInvalid(status);
        var applied = historyEntries
            .Select(item => item.MigrationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = migrations
            .Where(item => applied.Contains(item.Id))
            .Reverse()
            .Take(steps);

        foreach (var migration in selected)
        {
            await RenewLockAsync(lockHandle, cancellationToken);
            await using var transaction = await session.BeginTransactionAsync(
                cancellationToken: cancellationToken);
            var planned = await PlanDetailsAsync(
                migration, MigrationDirection.Down, transaction, cancellationToken);
            await ExecuteAsync(transaction, planned, cancellationToken);
            await history.DeleteAsync(transaction, migration.Id, cancellationToken);
            ThrowIfLockLost(lockHandle);
            await transaction.CommitAsync(cancellationToken);
            await RenewLockAsync(lockHandle, cancellationToken);
        }
    }

    private async Task<MigrationScript> PlanAsync(
        Migration migration,
        MigrationDirection direction,
        IDbExecutor executor,
        CancellationToken cancellationToken)
    {
        var result = await PlanDetailsAsync(
            migration, direction, executor, cancellationToken);
        return result.Script;
    }

    private async Task<PlannedMigration> PlanDetailsAsync(
        Migration migration,
        MigrationDirection direction,
        IDbExecutor executor,
        CancellationToken cancellationToken)
    {
        var plan = new MigrationPlan(dialect.ProviderName);
        var context = new MigrationContext(
            plan,
            dialect.CreateSchemaInspector(executor),
            cancellationToken);
        if (direction == MigrationDirection.Up)
        {
            await migration.UpAsync(context);
        }
        else if (direction == MigrationDirection.Down)
        {
            await migration.DownAsync(context);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
        }
        var operations = plan.Operations;
        return new PlannedMigration(
            dialect.Compiler.Compile(migration.Id, direction, plan),
            operations,
            context.Schema.WasAccessed);
    }

    private async Task<IReadOnlyList<MigrationStatusEntry>> BuildStatusAsync(
        IDbExecutor executor,
        IReadOnlyList<MigrationHistoryEntry> applied,
        CancellationToken cancellationToken)
    {
        var historyById = applied.ToDictionary(
            item => item.MigrationId,
            StringComparer.OrdinalIgnoreCase);
        var bundleById = migrations
            .Select((migration, index) => (migration, index))
            .ToDictionary(item => item.migration.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<MigrationStatusEntry>();

        for (var index = 0; index < migrations.Count; index++)
        {
            var migration = migrations[index];
            if (!historyById.TryGetValue(migration.Id, out var entry))
            {
                result.Add(new MigrationStatusEntry(
                    migration.Id, MigrationStatusState.Pending, index + 1));
                continue;
            }

            if (entry.AppliedOrder != index + 1)
            {
                result.Add(new MigrationStatusEntry(
                    migration.Id,
                    MigrationStatusState.OutOfOrder,
                    index + 1,
                    entry.AppliedOrder,
                    entry.Fingerprint));
                continue;
            }

            var planned = await PlanDetailsAsync(
                migration,
                MigrationDirection.Up,
                executor,
                cancellationToken);
            var state = planned.SchemaWasAccessed
                ? MigrationStatusState.Unverifiable
                : string.Equals(
                    entry.Fingerprint,
                    planned.Script.Fingerprint,
                    StringComparison.OrdinalIgnoreCase)
                    ? MigrationStatusState.Applied
                    : MigrationStatusState.Changed;
            result.Add(new MigrationStatusEntry(
                migration.Id,
                state,
                index + 1,
                entry.AppliedOrder,
                entry.Fingerprint,
                planned.Script.Fingerprint));
        }

        foreach (var entry in applied.Where(item => !bundleById.ContainsKey(item.MigrationId)))
        {
            result.Add(new MigrationStatusEntry(
                entry.MigrationId,
                MigrationStatusState.Missing,
                AppliedOrder: entry.AppliedOrder,
                StoredFingerprint: entry.Fingerprint));
        }

        return result.AsReadOnly();
    }

    private static void ThrowIfInvalid(IEnumerable<MigrationStatusEntry> status)
    {
        var invalid = status.Where(item => item.State is
            MigrationStatusState.Changed or
            MigrationStatusState.Missing or
            MigrationStatusState.OutOfOrder).ToArray();
        if (invalid.Length > 0)
        {
            throw new MigrationHistoryValidationException(invalid);
        }
    }

    private ValueTask<IAsyncDisposable> AcquireLockAsync(CancellationToken cancellationToken)
    {
        if (locking == MigrationLocking.Disabled)
        {
            return ValueTask.FromResult<IAsyncDisposable>(NoMigrationLockHandle.Instance);
        }
        if (migrationLock is null)
        {
            if (locking == MigrationLocking.Required)
            {
                throw new InvalidOperationException(
                    $"Dialect '{dialect.GetType().Name}' does not provide a migration lock. " +
                    "Supply MigrationRunnerOptions.MigrationLock or explicitly disable locking.");
            }
            return ValueTask.FromResult<IAsyncDisposable>(NoMigrationLockHandle.Instance);
        }
        return migrationLock.AcquireAsync(new MigrationLockContext(
            database,
            lockResource,
            historyTable,
            lockTimeout,
            lockLeaseDuration,
            cancellationToken));
    }

    private async Task ExecuteAsync(
        IDbExecutor executor,
        PlannedMigration planned,
        CancellationToken cancellationToken)
    {
        var resolver = new ConditionalOperationResolver(
            dialect.CreateSchemaInspector(executor));
        await resolver.PreloadAsync(planned.Operations, cancellationToken);
        foreach (var operation in planned.Operations)
        {
            if (!await resolver.ShouldExecuteAsync(operation, cancellationToken))
            {
                continue;
            }
            var script = dialect.Compiler.Compile(
                planned.Script.MigrationId,
                planned.Script.Direction,
                MigrationPlan.ForOperation(operation));
            foreach (var statement in script.Statements)
            {
                await executor.ExecuteAsync(
                    statement.Sql, cancellationToken: cancellationToken);
            }
            resolver.RecordExecuted(operation);
        }
    }

    private static void ThrowIfLockLost(IAsyncDisposable lockHandle)
    {
        if (lockHandle is IMigrationLockStatus status)
        {
            status.ThrowIfLost();
        }
    }

    private static ValueTask RenewLockAsync(
        IAsyncDisposable lockHandle,
        CancellationToken cancellationToken) =>
        lockHandle is IMigrationLockStatus status
            ? status.RenewAsync(cancellationToken)
            : ValueTask.CompletedTask;

    private static IReadOnlyList<Migration> SnapshotAndValidate(
        IEnumerable<Migration> migrations)
    {
        var snapshot = migrations.ToArray();
        if (snapshot.Any(migration => migration is null))
        {
            throw new ArgumentException("Migrations cannot contain null values.", nameof(migrations));
        }

        var duplicate = snapshot
            .GroupBy(migration => migration.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Migration ID '{duplicate.Key}' is already registered.");
        }
        return Array.AsReadOnly(snapshot);
    }

    private sealed record PlannedMigration(
        MigrationScript Script,
        IReadOnlyList<MigrationOperation> Operations,
        bool SchemaWasAccessed);
}
