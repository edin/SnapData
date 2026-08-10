using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SnapData.Benchmarks;

[MemoryDiagnoser]
public class CommandBenchmarks
{
    private const string Sql =
        "UPDATE counters SET value = value + 1 WHERE id = @Id";
    private SqliteConnection _connection = null!;
    private DbSession _session = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _session = DbSession.Borrow(_connection, SqliteQueryCompiler.Instance);
        await _connection.ExecuteAsync(
            "CREATE TABLE counters (id INTEGER PRIMARY KEY, value INTEGER NOT NULL);"
            + "INSERT INTO counters VALUES (1, 0);");
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _session.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Benchmark]
    public Task<int> SnapDataExecute() =>
        _session.ExecuteAsync(Sql, new { Id = 1 });

    [Benchmark(Baseline = true)]
    public Task<int> DapperExecute() =>
        _connection.ExecuteAsync(Sql, new { Id = 1 });
}
