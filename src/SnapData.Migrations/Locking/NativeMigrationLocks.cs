using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;

namespace SnapData.Migrations;

public sealed class SqlServerMigrationLock : IMigrationLock
{
    public static SqlServerMigrationLock Instance { get; } = new();
    private SqlServerMigrationLock() { }

    public async ValueTask<IAsyncDisposable> AcquireAsync(MigrationLockContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var session = await context.Database.OpenSessionAsync(context.CancellationToken);
        try
        {
            var resource = NativeLockNames.Limit(context.Resource, 255);
            var milliseconds = checked((int)Math.Min(
                context.Timeout.TotalMilliseconds, int.MaxValue));
            var result = await session.ScalarAsync<int>(
                "DECLARE @result int; EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @milliseconds; SELECT @result",
                new { resource, milliseconds },
                new QueryOptions { CommandTimeout = NativeLockNames.CommandTimeout(context.Timeout) },
                context.CancellationToken);
            if (result < 0)
            {
                throw new MigrationLockTimeoutException(context.Resource, context.Timeout);
            }
            return new MigrationLockHandle(session, async owner =>
            {
                await owner.ExecuteAsync(
                    "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session'",
                    new { resource });
            });
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}

public sealed class PostgresMigrationLock : IMigrationLock
{
    public static PostgresMigrationLock Instance { get; } = new();
    private PostgresMigrationLock() { }

    public async ValueTask<IAsyncDisposable> AcquireAsync(MigrationLockContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var session = await context.Database.OpenSessionAsync(context.CancellationToken);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            while (!await session.ScalarAsync<bool>(
                "SELECT pg_try_advisory_lock(hashtextextended(@resource, 0))",
                new { resource = context.Resource },
                cancellationToken: context.CancellationToken))
            {
                if (stopwatch.Elapsed >= context.Timeout)
                {
                    throw new MigrationLockTimeoutException(context.Resource, context.Timeout);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), context.CancellationToken);
            }
            return new MigrationLockHandle(session, async owner =>
            {
                await owner.ScalarAsync<bool>(
                    "SELECT pg_advisory_unlock(hashtextextended(@resource, 0))",
                    new { resource = context.Resource });
            });
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}

public sealed class MySqlMigrationLock : IMigrationLock
{
    public static MySqlMigrationLock Instance { get; } = new();
    private MySqlMigrationLock() { }

    public async ValueTask<IAsyncDisposable> AcquireAsync(MigrationLockContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var session = await context.Database.OpenSessionAsync(context.CancellationToken);
        try
        {
            var resource = NativeLockNames.Limit(context.Resource, 64);
            var seconds = Math.Max(0, checked((int)Math.Min(
                Math.Ceiling(context.Timeout.TotalSeconds), int.MaxValue)));
            var result = await session.ScalarAsync<long>(
                "SELECT GET_LOCK(@resource, @seconds)",
                new { resource, seconds },
                new QueryOptions { CommandTimeout = NativeLockNames.CommandTimeout(context.Timeout) },
                context.CancellationToken);
            if (result != 1)
            {
                throw new MigrationLockTimeoutException(context.Resource, context.Timeout);
            }
            return new MigrationLockHandle(session, async owner =>
            {
                await owner.ScalarAsync<long>(
                    "SELECT RELEASE_LOCK(@resource)",
                    new { resource });
            });
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}

internal static class NativeLockNames
{
    public static string Limit(string resource, int maximumLength)
    {
        if (resource.Length <= maximumLength)
        {
            return resource;
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource)));
        return resource[..(maximumLength - hash.Length - 1)] + ":" + hash;
    }

    public static int CommandTimeout(TimeSpan timeout) =>
        Math.Max(1, checked((int)Math.Min(Math.Ceiling(timeout.TotalSeconds) + 5, int.MaxValue)));
}
