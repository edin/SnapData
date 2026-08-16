namespace SnapData.Migrations;

public enum MigrationLocking
{
    Required,
    Auto,
    Disabled
}

public interface IMigrationLock
{
    ValueTask<IAsyncDisposable> AcquireAsync(MigrationLockContext context);
}

public sealed class MigrationLockContext
{
    internal MigrationLockContext(
        SnapDatabase database,
        string resource,
        string historyTable,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Database = database;
        Resource = resource;
        HistoryTable = historyTable;
        Timeout = timeout;
        CancellationToken = cancellationToken;
    }

    public SnapDatabase Database { get; }
    public string Resource { get; }
    public string HistoryTable { get; }
    public TimeSpan Timeout { get; }
    public CancellationToken CancellationToken { get; }
}

public sealed class MigrationLockTimeoutException(string resource, TimeSpan timeout)
    : TimeoutException($"Could not acquire migration lock '{resource}' within {timeout}.")
{
    public string Resource { get; } = resource;
    public TimeSpan Timeout { get; } = timeout;
}

internal sealed class MigrationLockHandle(
    DbSession session,
    Func<DbSession, ValueTask> release) : IAsyncDisposable
{
    private int disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await release(session);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}

internal sealed class NoMigrationLockHandle : IAsyncDisposable
{
    public static NoMigrationLockHandle Instance { get; } = new();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
