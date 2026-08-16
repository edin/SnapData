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
    private readonly string lockResource;
    private readonly string historyTable;

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

        this.database = database;
        this.migrations = SnapshotAndValidate(migrations);
        this.dialect = dialect;
        locking = options.Locking;
        migrationLock = options.MigrationLock ?? dialect.MigrationLock;
        lockTimeout = options.LockTimeout;
        historyTable = options.HistoryTable;
        lockResource = string.IsNullOrWhiteSpace(options.LockResource)
            ? $"SnapData.Migrations:{options.HistoryTable}"
            : options.LockResource;
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

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var lockHandle = await AcquireLockAsync(cancellationToken);
        await using var session = await database.OpenSessionAsync(cancellationToken);
        await history.EnsureCreatedAsync(session, cancellationToken);
        var applied = (await history.ReadAsync(session, cancellationToken))
            .Select(item => item.MigrationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var migration in migrations.Where(item => !applied.Contains(item.Id)))
        {
            await using var transaction = await session.BeginTransactionAsync(
                cancellationToken: cancellationToken);
            var script = await PlanAsync(
                migration, MigrationDirection.Up, transaction, cancellationToken);
            await ExecuteAsync(transaction, script, cancellationToken);
            await history.InsertAsync(
                transaction, migration.Id, DateTimeOffset.UtcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task RollbackAsync(
        int steps = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        await using var lockHandle = await AcquireLockAsync(cancellationToken);
        await using var session = await database.OpenSessionAsync(cancellationToken);
        await history.EnsureCreatedAsync(session, cancellationToken);
        var applied = (await history.ReadAsync(session, cancellationToken))
            .Select(item => item.MigrationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = migrations
            .Where(item => applied.Contains(item.Id))
            .Reverse()
            .Take(steps);

        foreach (var migration in selected)
        {
            await using var transaction = await session.BeginTransactionAsync(
                cancellationToken: cancellationToken);
            var script = await PlanAsync(
                migration, MigrationDirection.Down, transaction, cancellationToken);
            await ExecuteAsync(transaction, script, cancellationToken);
            await history.DeleteAsync(transaction, migration.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<MigrationScript> PlanAsync(
        Migration migration,
        MigrationDirection direction,
        IDbExecutor executor,
        CancellationToken cancellationToken)
    {
        var plan = new MigrationPlan();
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
        return dialect.Compiler.Compile(migration.Id, direction, plan);
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
            cancellationToken));
    }

    private static async Task ExecuteAsync(
        IDbExecutor executor,
        MigrationScript script,
        CancellationToken cancellationToken)
    {
        foreach (var statement in script.Statements)
        {
            await executor.ExecuteAsync(statement.Sql, cancellationToken: cancellationToken);
        }
    }

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
}
