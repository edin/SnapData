using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SnapData.Benchmarks;

public abstract class SqliteQueryBenchmarkBase
{
    protected const string SingleSql =
        "SELECT id, name, active FROM users WHERE id = @Id";
    protected const string ManySql =
        "SELECT id, name, active FROM users WHERE id <= @RowCount ORDER BY id";
    protected SqliteConnection Connection { get; private set; } = null!;
    protected DbSession Session { get; private set; } = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        await Connection.OpenAsync();
        Session = DbSession.Borrow(Connection, SqliteQueryCompiler.Instance);

        await Connection.ExecuteAsync(
            """
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                active INTEGER NOT NULL
            );
            """);

        await using var transaction = await Connection.BeginTransactionAsync();
        for (var id = 1; id <= 1000; id++)
        {
            await Connection.ExecuteAsync(
                "INSERT INTO users (id, name, active) VALUES (@Id, @Name, @Active)",
                new { Id = id, Name = $"User {id}", Active = id % 2 == 0 },
                transaction);
        }

        await transaction.CommitAsync();

        // Warm both libraries so the benchmarks measure steady-state execution.
        _ = await Session.QuerySingleOrDefaultAsync<User>(SingleSql, new { Id = 1 });
        _ = await Connection.QuerySingleAsync<User>(SingleSql, new { Id = 1 });
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await Session.DisposeAsync();
        await Connection.DisposeAsync();
    }

    [Table("users")]
    public sealed class User
    {
        [Key]
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool Active { get; set; }
    }
}

[MemoryDiagnoser]
public class SingleQueryBenchmarks : SqliteQueryBenchmarkBase
{
    [Benchmark]
    public Task<User?> SnapData() =>
        Session.QuerySingleOrDefaultAsync<User>(SingleSql, new { Id = 500 });

    [Benchmark(Baseline = true)]
    public Task<User> Dapper() =>
        Connection.QuerySingleAsync<User>(SingleSql, new { Id = 500 });
}

[MemoryDiagnoser]
public class BufferedQueryBenchmarks : SqliteQueryBenchmarkBase
{
    [Params(10, 100, 1000)]
    public int RowCount { get; set; }

    [Benchmark]
    public Task<IReadOnlyList<User>> SnapDataRawSql() =>
        Session.QueryAsync<User>(ManySql, new { RowCount });

    [Benchmark(Baseline = true)]
    public async Task<List<User>> Dapper() =>
        (await Connection.QueryAsync<User>(ManySql, new { RowCount })).AsList();

    [Benchmark]
    public Task<IReadOnlyList<User>> SnapDataTyped() =>
        Session
            .From<User>()
            .Where(user => user.Id <= RowCount)
            .OrderBy(user => user.Id)
            .ToListAsync();
}
