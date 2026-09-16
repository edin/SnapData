using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace SnapData.Tests;

public sealed class CleanupFailureTests
{
    [Fact]
    public async Task Opening_failure_is_not_masked_by_connection_disposal()
    {
        var connection = new FaultConnection { FailOpen = true, FailConnectionCleanup = true };
        var database = new SnapDatabase(new FaultAdapter(connection));
        var error = await Assert.ThrowsAsync<ApplicationException>(() => database.OpenSessionAsync().AsTask());
        Assert.Same(connection.OpenFailure, error);
        Assert.Equal(1, connection.DisposeCount);
        Assert.Same(connection.ConnectionCleanupFailure,
            Assert.Single(Assert.IsType<List<Exception>>(error.Data[CleanupErrors.DataKey])));
    }

    [Theory]
    [InlineData(false, "owned")]
    [InlineData(true, "owned")]
    [InlineData(false, "borrowed-closed")]
    [InlineData(true, "borrowed-closed")]
    [InlineData(false, "borrowed-open")]
    [InlineData(true, "borrowed-open")]
    public async Task Session_disposal_attempts_all_owned_cleanup_and_preserves_first_error(bool asynchronous, string ownership)
    {
        var connection = new FaultConnection { FailConnectionCleanup = true };
        if (ownership == "borrowed-open") connection.Open();
        var session = ownership == "owned"
            ? DbSession.Own(connection, SqliteQueryCompiler.Instance, new EntityMappingProvider(), null)
            : DbSession.Borrow(connection);
        var transaction = await session.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DisposeAsync(session, asynchronous));
        Assert.Same(connection.RollbackFailure, error);
        var secondary = Assert.IsType<List<Exception>>(error.Data[CleanupErrors.DataKey]);
        Assert.Same(connection.TransactionDisposeFailure, secondary[0]);
        Assert.Equal(ownership == "borrowed-open" ? 1 : 2, secondary.Count);
        if (ownership != "borrowed-open") Assert.Same(connection.ConnectionCleanupFailure, secondary[1]);
        Assert.Equal(1, connection.Transaction!.RollbackCount);
        Assert.Equal(1, connection.Transaction.DisposeCount);
        Assert.Equal(ownership == "owned" ? 1 : 0, connection.DisposeCount);
        Assert.Equal(ownership == "borrowed-closed" ? 1 : 0, connection.CloseCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transaction.CommitAsync().AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.BeginTransactionAsync().AsTask());
        await DisposeAsync(session, asynchronous);
        await DisposeAsync(transaction, asynchronous);
        Assert.Equal(1, connection.Transaction.DisposeCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Failed_transaction_cleanup_faults_session_even_after_commit(bool asynchronous, bool commit)
    {
        var connection = new FaultConnection();
        connection.Open();
        var session = DbSession.Borrow(connection);
        var transaction = await session.BeginTransactionAsync();
        if (commit) await transaction.CommitAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DisposeAsync(transaction, asynchronous));
        Assert.Same(commit ? connection.TransactionDisposeFailure : connection.RollbackFailure, error);
        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => session.BeginTransactionAsync().AsTask());
        Assert.Contains("connection state is uncertain", rejected.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteAsync("SELECT 1"));
        await DisposeAsync(session, asynchronous);
        Assert.Equal(0, connection.CloseCount);
        Assert.Equal(0, connection.DisposeCount);
    }

    [Fact]
    public async Task Owned_batch_preserves_operation_failure_when_iterator_and_transaction_cleanup_fail()
    {
        var connection = new FaultConnection();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        var input = new FaultInput();
        var error = await Assert.ThrowsAsync<ApplicationException>(() => session.InsertManyAsync(input));
        Assert.Same(input.Failure, error);
        Assert.Equal(1, input.DisposeCount);
        Assert.Equal(1, connection.Transaction!.RollbackCount);
        Assert.Equal(1, connection.Transaction.DisposeCount);
        var iteratorError = Assert.Single(Assert.IsType<List<Exception>>(error.Data[CleanupErrors.DataKey]));
        Assert.Same(input.DisposeFailure, iteratorError);
        var rollbackError = Assert.Single(Assert.IsType<List<Exception>>(iteratorError.Data[CleanupErrors.DataKey]));
        Assert.Same(connection.RollbackFailure, rollbackError);
        Assert.Same(connection.TransactionDisposeFailure,
            Assert.Single(Assert.IsType<List<Exception>>(rollbackError.Data[CleanupErrors.DataKey])));
    }

    [Fact]
    public async Task Owned_batch_preserves_cancellation_type_and_token_when_rollback_fails()
    {
        var connection = new FaultConnection();
        await using var session = DbSession.Borrow(connection, SqliteQueryCompiler.Instance);
        using var cancel = new CancellationTokenSource();
        var input = new FaultInput
        {
            BeforeRead = cancel.Cancel,
            Failure = new OperationCanceledException(cancel.Token),
            ThrowOnDispose = false
        };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InsertManyAsync(input,
            cancellationToken: cancel.Token));
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.Same(connection.RollbackFailure,
            Assert.Single(Assert.IsType<List<Exception>>(error.Data[CleanupErrors.DataKey])));
    }

    private static async Task DisposeAsync(IDisposable resource, bool asynchronous)
    {
        if (asynchronous) await ((IAsyncDisposable)resource).DisposeAsync();
        else resource.Dispose();
    }

    private sealed class FaultConnection : DbConnection
    {
        private ConnectionState _state;
        public InvalidOperationException RollbackFailure { get; } = new("rollback failed");
        public InvalidOperationException TransactionDisposeFailure { get; } = new("transaction dispose failed");
        public InvalidOperationException ConnectionCleanupFailure { get; } = new("connection cleanup failed");
        public bool FailConnectionCleanup { get; init; }
        public bool FailOpen { get; init; }
        public ApplicationException OpenFailure { get; } = new("open failed");
        public int DisposeCount { get; private set; }
        public int CloseCount { get; private set; }
        public FaultTransaction? Transaction { get; private set; }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void Open()
        {
            if (FailOpen) throw OpenFailure;
            _state = ConnectionState.Open;
        }
        public override void Close()
        {
            CloseCount++;
            _state = ConnectionState.Closed;
            if (FailConnectionCleanup) throw ConnectionCleanupFailure;
        }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => Transaction = new(this);
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            DisposeCount++;
            _state = ConnectionState.Closed;
            if (FailConnectionCleanup) throw ConnectionCleanupFailure;
        }
    }

    private sealed class FaultTransaction(FaultConnection connection) : DbTransaction
    {
        public int RollbackCount { get; private set; }
        public int DisposeCount { get; private set; }
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { RollbackCount++; throw connection.RollbackFailure; }
        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            DisposeCount++;
            throw connection.TransactionDisposeFailure;
        }
    }

    private sealed class FaultInput : IEnumerable<Item>, IEnumerator<Item>
    {
        public Exception Failure { get; init; } = new ApplicationException("input failed");
        public Action? BeforeRead { get; init; }
        public bool ThrowOnDispose { get; init; } = true;
        public InvalidOperationException DisposeFailure { get; } = new("iterator dispose failed");
        public int DisposeCount { get; private set; }
        public Item Current { get { BeforeRead?.Invoke(); throw Failure; } }
        object IEnumerator.Current => Current;
        public IEnumerator<Item> GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public bool MoveNext() => true;
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { DisposeCount++; if (ThrowOnDispose) throw DisposeFailure; }
    }

    private sealed class Item { public int Id { get; set; } }

    private sealed class FaultAdapter(FaultConnection connection) : IDatabaseAdapter
    {
        public IQueryCompiler QueryCompiler => SqliteQueryCompiler.Instance;
        public bool CanUse(DbConnection candidate) => ReferenceEquals(candidate, connection);
        public DbConnection CreateConnection() => connection;
    }
}
