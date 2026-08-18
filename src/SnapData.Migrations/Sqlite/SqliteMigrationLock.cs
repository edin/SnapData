using System.Data.Common;
using System.Diagnostics;
using System.Globalization;

namespace SnapData.Migrations;

public sealed class SqliteMigrationLock : IMigrationLock
{
    public static SqliteMigrationLock Instance { get; } = new();

    private SqliteMigrationLock()
    {
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(MigrationLockContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var session = await context.Database.OpenSessionAsync(context.CancellationToken);
        var table = LockTable(context.HistoryTable);
        var quotedTable = QuoteQualified(table);
        try
        {
            await session.ExecuteAsync(
                $"CREATE TABLE IF NOT EXISTS {quotedTable} (" +
                "\"resource\" TEXT NOT NULL PRIMARY KEY, " +
                "\"owner_id\" TEXT NOT NULL, " +
                "\"expires_at\" TEXT NOT NULL)",
                options: CommandOptions(context.Timeout),
                cancellationToken: context.CancellationToken);

            var ownerId = Guid.NewGuid().ToString("N");
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                var now = DateTime.UtcNow;
                var expiresAt = now.Add(context.LeaseDuration);
                try
                {
                    var affected = await session.ExecuteAsync(
                        $"INSERT INTO {quotedTable} (\"resource\", \"owner_id\", \"expires_at\") " +
                        "VALUES (@resource, @ownerId, @expiresAt) " +
                        "ON CONFLICT(\"resource\") DO UPDATE SET " +
                        "\"owner_id\" = excluded.\"owner_id\", " +
                        "\"expires_at\" = excluded.\"expires_at\" " +
                        "WHERE \"expires_at\" <= @now",
                        new
                        {
                            resource = context.Resource,
                            ownerId,
                            now = Timestamp(now),
                            expiresAt = Timestamp(expiresAt)
                        },
                        CommandOptions(context.Timeout),
                        context.CancellationToken);
                    if (affected == 1)
                    {
                        return new SqliteMigrationLockHandle(
                            session,
                            quotedTable,
                            context.Resource,
                            ownerId,
                            context.LeaseDuration,
                            expiresAt);
                    }
                }
                catch (DbException)
                {
                    // A concurrent SQLite writer may temporarily hold the database lock.
                }

                if (stopwatch.Elapsed >= context.Timeout)
                {
                    throw new MigrationLockTimeoutException(
                        context.Resource, context.Timeout);
                }

                var remaining = context.Timeout - stopwatch.Elapsed;
                await Task.Delay(
                    remaining < TimeSpan.FromMilliseconds(100)
                        ? remaining
                        : TimeSpan.FromMilliseconds(100),
                    context.CancellationToken);
            }
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static QueryOptions CommandOptions(TimeSpan timeout) => new()
    {
        CommandTimeout = Math.Max(
            1,
            checked((int)Math.Min(Math.Ceiling(timeout.TotalSeconds), int.MaxValue)))
    };

    private static string LockTable(string historyTable)
    {
        var parts = historyTable.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A SQLite history table must use 'table' or 'schema.table' form.",
                nameof(historyTable));
        }
        parts[^1] += "_lock";
        return string.Join('.', parts);
    }

    private static string QuoteQualified(string value) => string.Join(
        ".",
        value.Split('.').Select(part =>
            $"\"{part.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));

    private static string Timestamp(DateTime value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private sealed class SqliteMigrationLockHandle :
        IAsyncDisposable,
        IMigrationLockStatus
    {
        private readonly DbSession session;
        private readonly string table;
        private readonly string resource;
        private readonly string ownerId;
        private readonly TimeSpan leaseDuration;
        private readonly TimeSpan renewalInterval;
        private readonly CancellationTokenSource stopping = new();
        private readonly SemaphoreSlim renewalGate = new(1, 1);
        private readonly Task heartbeat;
        private Exception? lost;
        private long expiresAtUtcTicks;
        private int disposed;

        public SqliteMigrationLockHandle(
            DbSession session,
            string table,
            string resource,
            string ownerId,
            TimeSpan leaseDuration,
            DateTime expiresAtUtc)
        {
            this.session = session;
            this.table = table;
            this.resource = resource;
            this.ownerId = ownerId;
            this.leaseDuration = leaseDuration;
            expiresAtUtcTicks = expiresAtUtc.Ticks;
            var proposed = TimeSpan.FromTicks(leaseDuration.Ticks / 3);
            renewalInterval = proposed < TimeSpan.FromMilliseconds(100)
                ? TimeSpan.FromMilliseconds(100)
                : proposed > TimeSpan.FromSeconds(30)
                    ? TimeSpan.FromSeconds(30)
                    : proposed;
            heartbeat = RenewAsync();
        }

        public void ThrowIfLost()
        {
            if (Volatile.Read(ref lost) is not null)
            {
                throw new MigrationLockLostException(resource);
            }
        }

        public async ValueTask RenewAsync(CancellationToken cancellationToken)
        {
            ThrowIfLost();
            await renewalGate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfLost();
                var expiresAt = DateTime.UtcNow.Add(leaseDuration);
                var affected = await session.ExecuteAsync(
                    $"UPDATE {table} SET \"expires_at\" = @expiresAt " +
                    "WHERE \"resource\" = @resource AND \"owner_id\" = @ownerId",
                    new
                    {
                        expiresAt = Timestamp(expiresAt),
                        resource,
                        ownerId
                    },
                    RenewalCommandOptions(),
                    cancellationToken);
                if (affected != 1)
                {
                    MarkLost();
                    ThrowIfLost();
                }
                Interlocked.Exchange(ref expiresAtUtcTicks, expiresAt.Ticks);
            }
            finally
            {
                renewalGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            await stopping.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }

            try
            {
                await session.ExecuteAsync(
                    $"DELETE FROM {table} WHERE \"resource\" = @resource " +
                    "AND \"owner_id\" = @ownerId",
                    new { resource, ownerId });
            }
            finally
            {
                stopping.Dispose();
                renewalGate.Dispose();
                await session.DisposeAsync();
            }

            ThrowIfLost();
        }

        private async Task RenewAsync()
        {
            while (!stopping.IsCancellationRequested)
            {
                await Task.Delay(renewalInterval, stopping.Token);
                try
                {
                    await RenewAsync(stopping.Token);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (MigrationLockLostException)
                {
                    return;
                }
                catch (DbException exception)
                {
                    if (DateTime.UtcNow.Ticks >= Interlocked.Read(ref expiresAtUtcTicks))
                    {
                        MarkLost(exception);
                        return;
                    }
                }
            }
        }

        private QueryOptions RenewalCommandOptions() => new()
        {
            CommandTimeout = Math.Max(
                1,
                checked((int)Math.Ceiling(
                    Math.Min(renewalInterval.TotalSeconds, 5))))
        };

        private void MarkLost(Exception? exception = null) =>
            Interlocked.CompareExchange(
                ref lost,
                exception ?? new MigrationLockLostException(resource),
                null);
    }
}
