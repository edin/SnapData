using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class InsertManyTests
{
    [Fact]
    public async Task Batches_mapped_values_with_parameter_budget_and_does_not_hydrate_ids()
    {
        await using var connection = await OpenAsync();
        var observer = new Observer();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance, commandObserver: observer);
        var input = Enumerable.Range(1, 5).Select(i => new Item { Name = "name" + i, Note = "note" }).ToArray();
        var result = await session.InsertManyAsync(input, new InsertManyOptions { BatchSize = 100, MaxParametersPerCommand = 4 },
            new QueryOptions { CommandTimeout = 17 });
        Assert.Equal(new InsertManyResult(5, 3, 5), result);
        Assert.All(input, item => Assert.Equal(0, item.Id));
        Assert.Equal(new[] { 4, 4, 2 }, observer.Started.Select(e => e.Command.Parameters.Count));
        Assert.All(observer.Started, e => Assert.True(e.HasTransaction));
        Assert.Equal(5, await session.From<Item>().CountAsync());
    }

    [Fact]
    public async Task Empty_input_and_impossible_budget_do_not_open_a_connection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        Assert.Equal(new InsertManyResult(0, 0, 0), await session.InsertManyAsync(Array.Empty<Item>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InsertManyAsync(new[] { new Item() },
            new InsertManyOptions { MaxParametersPerCommand = 1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.InsertManyAsync(Array.Empty<Item>(), new InsertManyOptions { BatchSize = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.InsertManyAsync(Array.Empty<Item>(), new InsertManyOptions { MaxParametersPerCommand = 0 }));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Atomic_and_non_atomic_failure_have_explicit_outcomes(bool atomic, long expectedRows)
    {
        await using var connection = await OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var input = new[] { new Item { Name = "duplicate" }, new Item { Name = "duplicate" } };
        await Assert.ThrowsAsync<SqliteException>(() => session.InsertManyAsync(input,
            new InsertManyOptions { BatchSize = 1, Atomic = atomic }));
        Assert.Equal(expectedRows, await session.From<Item>().CountAsync());
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Enumeration_failure_rolls_back_only_owned_atomic_work(bool atomic, long expectedRows)
    {
        await using var connection = await OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        static IEnumerable<Item> Input()
        {
            yield return new Item { Name = "first" };
            throw new InvalidOperationException("source failed");
        }
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.InsertManyAsync(Input(),
            new InsertManyOptions { BatchSize = 1, Atomic = atomic }));
        Assert.Equal("source failed", error.Message);
        Assert.Equal(expectedRows, await session.From<Item>().CountAsync());
    }

    [Fact]
    public async Task Caller_transaction_remains_owned_by_caller()
    {
        await using var connection = await OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await using var transaction = await session.BeginTransactionAsync();
        await transaction.InsertManyAsync(new[] { new Item { Name = "first" }, new Item { Name = "second" } });
        Assert.Equal(2, await transaction.From<Item>().CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.InsertManyAsync(new[] { new Item { Name = "wrong session" } }));
        await transaction.RollbackAsync();
        Assert.Equal(0, await session.From<Item>().CountAsync());
    }

    [Fact]
    public async Task Cancellation_between_batches_rolls_back_and_enumerator_is_disposed()
    {
        await using var connection = await OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        using var cancel = new CancellationTokenSource();
        var disposed = false;
        IEnumerable<Item> Input()
        {
            try
            {
                yield return new Item { Name = "first" };
                cancel.Cancel();
                yield return new Item { Name = "second" };
            }
            finally { disposed = true; }
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InsertManyAsync(Input(),
            new InsertManyOptions { BatchSize = 1 }, cancellationToken: cancel.Token));
        Assert.True(disposed);
        Assert.Equal(0, await session.From<Item>().CountAsync());
    }

    [Fact]
    public async Task Lazy_reused_entities_are_snapshotted_without_reading_ahead_of_batches()
    {
        await using var connection = await OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        static IEnumerable<Item> Input()
        {
            var item = new Item();
            for (var i = 0; i < 5; i++) { item.Name = "name" + i; yield return item; }
        }
        await session.InsertManyAsync(Input(), new InsertManyOptions { BatchSize = 2 });
        Assert.Equal(5, await session.From<Item>().CountAsync());
        Assert.Equal(new[] { "name0", "name1", "name2", "name3", "name4" },
            (await session.From<Item>().OrderBy(i => i.Name).ToListAsync()).Select(i => i.Name));
    }

    [Fact]
    public async Task Generated_only_entities_use_one_default_insert_each()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await session.ExecuteAsync("CREATE TABLE only_ids (id INTEGER PRIMARY KEY)");
        var result = await session.InsertManyAsync(new[] { new OnlyId(), new OnlyId() });
        Assert.Equal(new InsertManyResult(2, 2, 2), result);
    }

    [Fact]
    public async Task Provider_row_and_parameter_limits_bound_batches()
    {
        await using var connection = await OpenAsync();
        await using var session = DbSession.Borrow(connection, new LimitedCompiler());
        var result = await session.InsertManyAsync(Enumerable.Range(1, 5).Select(i => new Item { Name = "name" + i }),
            new InsertManyOptions { BatchSize = 99, MaxParametersPerCommand = 99 });
        Assert.Equal(3, result.StatementsExecuted);
        Assert.Throws<NotSupportedException>(() => Sql.InsertInto("items").Rows(new { id = 1 }, new { id = 2 }).Build(FirebirdQueryCompiler.Instance));
    }

    [Fact]
    public async Task Null_element_rolls_back_preceding_batches()
    {
        await using var connection = await OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        await Assert.ThrowsAsync<ArgumentNullException>(() => session.InsertManyAsync(
            new[] { new Item { Name = "first" }, null! }, new InsertManyOptions { BatchSize = 1 }));
        Assert.Equal(0, await session.From<Item>().CountAsync());
    }

    [Fact]
    public async Task Enumerator_disposal_failure_prevents_owned_commit()
    {
        await using var connection = await OpenAsync();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var input = new FailingDisposeInput();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.InsertManyAsync(input));
        Assert.Equal("dispose failed", error.Message);
        Assert.Equal(1, input.DisposeCount);
        Assert.Equal(0, await session.From<Item>().CountAsync());
    }

    private sealed class FailingDisposeInput : IEnumerable<Item>, IEnumerator<Item>
    {
        private bool _moved;
        public int DisposeCount { get; private set; }
        public Item Current { get; } = new() { Name = "first" };
        object System.Collections.IEnumerator.Current => Current;
        public IEnumerator<Item> GetEnumerator() => this;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool MoveNext() { if (_moved) return false; _moved = true; return true; }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { DisposeCount++; throw new InvalidOperationException("dispose failed"); }
    }

    private sealed class LimitedCompiler : IQueryCompiler
    {
        public QueryCapabilities Capabilities => new() { MultiRowInsert = true, MaxRowsPerInsert = 3, MaxParametersPerCommand = 4 };
        public SqlQuery Compile(ISqlQueryBuilder query) => SqliteQueryCompiler.Instance.Compile(query);
    }

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, display_name TEXT UNIQUE, note TEXT, computed INTEGER GENERATED ALWAYS AS (length(display_name)) STORED)";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private sealed class Observer : CommandObserver
    {
        public List<CommandExecutingContext> Started { get; } = [];
        public override void Executing(CommandExecutingContext context) => Started.Add(context);
    }

    [Table("items")]
    private sealed class Item
    {
        [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
        [Column("display_name")] public string Name { get; set; } = "";
        [Column("note")] public string Note { get; set; } = "";
        [Column("computed"), DatabaseGenerated(DatabaseGeneratedOption.Computed)] public int Computed { get; set; }
    }
    [Table("only_ids")]
    private sealed class OnlyId
    {
        [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.Identity)] public long Id { get; set; }
    }
}
