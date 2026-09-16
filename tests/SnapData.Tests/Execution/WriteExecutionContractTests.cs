using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace SnapData.Tests;

public sealed class WriteExecutionContractTests
{
    public static IEnumerable<object[]> ReturningCases =>
        from operation in new[] { "insert", "update", "delete", "upsert" }
        from terminal in new[] { "list", "single", "optional" }
        from transactional in new[] { false, true }
        select new object[] { operation, terminal, transactional };

    [Theory, MemberData(nameof(ReturningCases))]
    public async Task Returning_terminals_forward_options_tokens_and_observe_one_query(
        string operation, string terminal, bool transactional)
    {
        using var cancel = new CancellationTokenSource();
        var connection = new ProbeConnection();
        var observer = new ProbeObserver();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance, commandObserver: observer);
        await using var transaction = transactional ? await session.BeginTransactionAsync(cancellationToken: cancel.Token) : null;
        IDbExecutor executor = transaction is null ? session : transaction;
        var result = Returning(executor, operation);
        var options = new QueryOptions { CommandTimeout = 23 };
        var value = terminal switch
        {
            "list" => (await result.ToListAsync(options, cancel.Token)).Single(),
            "single" => await result.SingleAsync(options, cancel.Token),
            _ => await result.SingleOrDefaultAsync(options, cancel.Token)
        };
        Assert.Equal(17, value);
        var command = Assert.Single(connection.Commands);
        CheckCommand(command, cancel.Token);
        Assert.True(command.UsedReader);
        Assert.Equal(transactional, Assert.Single(observer.Started).HasTransaction);
        var completed = Assert.Single(observer.Completed);
        Assert.Equal(CommandExecutionKind.Query, completed.Kind);
        Assert.Equal(1, completed.ResultCount);
        Assert.Empty(observer.FailedEvents);
    }

    [Theory]
    [InlineData("upsert")]
    [InlineData("default-typed")]
    [InlineData("default-entity")]
    [InlineData("assignment")]
    public async Task Nonquery_and_generated_writes_forward_execution_contract(string operation)
    {
        using var cancel = new CancellationTokenSource();
        var connection = new ProbeConnection();
        var observer = new ProbeObserver();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance, commandObserver: observer);
        await WriteAsync(session, operation, cancel.Token);
        CheckCommand(Assert.Single(connection.Commands), cancel.Token);
        var completed = Assert.Single(observer.Completed);
        Assert.Equal(CommandExecutionKind.NonQuery, completed.Kind);
        Assert.Equal(1, completed.AffectedRows);
        Assert.Single(observer.Started);
        Assert.Empty(observer.FailedEvents);
    }

    [Theory]
    [InlineData("upsert")]
    [InlineData("default-typed")]
    [InlineData("default-entity")]
    [InlineData("assignment")]
    [InlineData("returning")]
    public async Task Provider_cancellation_keeps_original_token_and_emits_failure(string operation)
    {
        using var cancel = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancel.Token);
        var connection = new ProbeConnection { Failure = expected, FailAt = 1 };
        var observer = new ProbeObserver();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance, commandObserver: observer);
        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WriteAsync(session, operation, cancel.Token));
        Assert.Same(expected, actual);
        CheckCommand(Assert.Single(connection.Commands), cancel.Token);
        Assert.Same(expected, Assert.Single(observer.FailedEvents).Exception);
        Assert.Single(observer.Started);
        Assert.Empty(observer.Completed);
    }

    [Fact]
    public async Task Batches_forward_each_command_and_report_unknown_provider_counts()
    {
        using var cancel = new CancellationTokenSource();
        var connection = new ProbeConnection { Counts = new Queue<int>(new[] { -1, 2, 1 }) };
        var observer = new ProbeObserver();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance, commandObserver: observer);
        var result = await session.InsertManyAsync(Enumerable.Range(1, 5).Select(i => new Item { Name = "n" + i }),
            new InsertManyOptions { BatchSize = 2 }, new QueryOptions { CommandTimeout = 23 }, cancel.Token);
        Assert.Equal(new InsertManyResult(5, 3, null), result);
        Assert.Equal(3, connection.Commands.Count);
        Assert.All(connection.Commands, command => CheckCommand(command, cancel.Token));
        Assert.Equal(new[] { 2, 2, 1 }, observer.Started.Select(e => e.Command.Parameters.Count));
        Assert.All(observer.Started, e => Assert.True(e.HasTransaction));
        Assert.Equal(3, observer.Completed.Count);
        Assert.Empty(observer.FailedEvents);
        Assert.Equal(1, connection.Transaction!.Commits);
        Assert.Equal(cancel.Token, connection.Transaction.CommitToken);
        Assert.Equal(cancel.Token, connection.BeginToken);
    }

    [Fact]
    public async Task Cancellation_in_second_batch_observes_partial_commands_and_rolls_back_owned_work()
    {
        using var cancel = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancel.Token);
        var connection = new ProbeConnection { Failure = expected, FailAt = 2 };
        var observer = new ProbeObserver();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance, commandObserver: observer);
        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InsertManyAsync(
            Enumerable.Range(1, 3).Select(i => new Item { Name = "n" + i }),
            new InsertManyOptions { BatchSize = 1 }, new QueryOptions { CommandTimeout = 23 }, cancel.Token));
        Assert.Same(expected, actual);
        Assert.Equal(2, connection.Commands.Count);
        Assert.All(connection.Commands, command => CheckCommand(command, cancel.Token));
        Assert.Equal(2, observer.Started.Count);
        Assert.Single(observer.Completed);
        Assert.Same(expected, Assert.Single(observer.FailedEvents).Exception);
        Assert.Equal(1, connection.Transaction!.Rollbacks);
        Assert.Equal(0, connection.Transaction.Commits);
    }

    private static MutationResult<int> Returning(IDbExecutor executor, string operation) => operation switch
    {
        "insert" => executor.InsertInto<Item>().Values(new Item { Name = "n" }).Returning<int>(i => i.Id),
        "update" => executor.Update<Item>().Set(i => i.Name, "n").Where(i => i.Id == 17).Returning<int>(i => i.Id),
        "delete" => executor.DeleteFrom<Item>().Where(i => i.Id == 17).Returning<int>(i => i.Id),
        _ => executor.UpsertInto<Item>().Values(new Item { Name = "n" }).OnConflict(i => i.Name).DoNothing().Returning<int>(i => i.Id)
    };

    private static async Task WriteAsync(IDbExecutor executor, string operation, CancellationToken token)
    {
        var options = new QueryOptions { CommandTimeout = 23 };
        switch (operation)
        {
            case "returning": await Returning(executor, "upsert").SingleAsync(options, token); break;
            case "default-typed": await executor.InsertInto<OnlyId>().DefaultValues().ExecuteAsync(options, token); break;
            case "default-entity":
                var entity = new OnlyId();
                await executor.InsertAsync(entity, options, token);
                Assert.Equal(17, entity.Id);
                break;
            case "assignment":
                await executor.Update<Item>().SetExpression(i => i.Name, new ParameterValueExpression("n"))
                    .Where(i => i.Id == 17).ExecuteAsync(options, token);
                break;
            default:
                await executor.UpsertInto<Item>().Values(new Item { Name = "n" }).OnConflict(i => i.Name)
                    .DoNothing().ExecuteAsync(options, token);
                break;
        }
    }

    private static void CheckCommand(ProbeCommand command, CancellationToken token)
    {
        Assert.Equal(23, command.CommandTimeout);
        Assert.Equal(token, command.ExecutionToken);
        Assert.True(command.WasDisposed);
    }

    private sealed class ProbeObserver : CommandObserver
    {
        public List<CommandExecutingContext> Started { get; } = [];
        public List<CommandExecutedContext> Completed { get; } = [];
        public List<CommandFailedContext> FailedEvents { get; } = [];
        public override void Executing(CommandExecutingContext context) => Started.Add(context);
        public override void Executed(CommandExecutedContext context) => Completed.Add(context);
        public override void Failed(CommandFailedContext context) => FailedEvents.Add(context);
    }

    private sealed class ProbeConnection : DbConnection
    {
        private ConnectionState state;
        public List<ProbeCommand> Commands { get; } = [];
        public Queue<int> Counts { get; init; } = new();
        public Exception? Failure { get; init; }
        public int FailAt { get; init; }
        public ProbeTransaction? Transaction { get; private set; }
        public CancellationToken BeginToken { get; private set; }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "probe";
        public override string DataSource => "probe";
        public override string ServerVersion => "1";
        public override ConnectionState State => state;
        public override void Open() => state = ConnectionState.Open;
        public override void Close() => state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => Transaction = new(this);
        protected override ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
        {
            BeginToken = cancellationToken;
            return ValueTask.FromResult(BeginDbTransaction(isolationLevel));
        }
        protected override DbCommand CreateDbCommand()
        {
            var command = new ProbeCommand(this);
            Commands.Add(command);
            return command;
        }
    }

    private sealed class ProbeTransaction(ProbeConnection connection) : DbTransaction
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public CancellationToken CommitToken { get; private set; }
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() => Commits++;
        public override void Rollback() => Rollbacks++;
        public override Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitToken = cancellationToken;
            Commit();
            return Task.CompletedTask;
        }
    }

    private sealed class ProbeCommand(ProbeConnection connection) : DbCommand
    {
        private readonly SqliteCommand parameterHolder = new();
        public bool WasDisposed { get; private set; }
        public bool UsedReader { get; private set; }
        public CancellationToken ExecutionToken { get; private set; }
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameterHolder.Parameters;
        public override void Cancel() { }
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new SqliteParameter();
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object ExecuteScalar() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            Record(cancellationToken);
            return Task.FromResult(connection.Counts.TryDequeue(out var count) ? count : 1);
        }
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            Record(cancellationToken);
            UsedReader = true;
            var table = new DataTable();
            table.Columns.Add("id", typeof(int));
            table.Rows.Add(17);
            return Task.FromResult<DbDataReader>(table.CreateDataReader());
        }
        private void Record(CancellationToken token)
        {
            ExecutionToken = token;
            if (connection.Failure is { } failure && connection.Commands.Count == connection.FailAt) throw failure;
        }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            if (disposing) parameterHolder.Dispose();
            base.Dispose(disposing);
        }
    }

    [Table("items")]
    private sealed class Item
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int Id { get; set; }
        public string Name { get; set; } = "";
    }
    [Table("only_ids")]
    private sealed class OnlyId
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int Id { get; set; }
    }
}
