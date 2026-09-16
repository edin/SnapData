using System.Data;
using System.Data.Common;

namespace SnapData;

public sealed class DbSession : DbExecutor, IDisposable, IAsyncDisposable
{
    private readonly bool _ownsConnection;
    private bool _openedBorrowedConnection;
    private DbTransactionSession? _activeTransaction;
    private bool _disposed;
    private bool _transactionCleanupFailed;

    private DbSession(
        DbConnection connection,
        bool ownsConnection,
        bool openedBorrowedConnection,
        IQueryCompiler? queryCompiler,
        IEntityMappingProvider? mappingProvider,
        ICommandObserver? commandObserver)
        : base(
            connection,
            queryCompiler: queryCompiler,
            mappingProvider: mappingProvider,
            commandObserver: commandObserver)
    {
        _ownsConnection = ownsConnection;
        _openedBorrowedConnection = openedBorrowedConnection;
    }

    internal static DbSession Borrow(
        DbConnection connection,
        IQueryCompiler? queryCompiler = null,
        IEntityMappingProvider? mappingProvider = null,
        ICommandObserver? commandObserver = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new DbSession(
            connection,
            ownsConnection: false,
            openedBorrowedConnection: false,
            queryCompiler,
            mappingProvider,
            commandObserver);
    }

    internal static DbSession Own(
        DbConnection connection,
        IQueryCompiler queryCompiler,
        IEntityMappingProvider mappingProvider,
        ICommandObserver? commandObserver) =>
        new(
            connection,
            ownsConnection: true,
            openedBorrowedConnection: false,
            queryCompiler,
            mappingProvider,
            commandObserver);

    public async ValueTask<DbTransactionSession> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default)
    {
        EnsureCanExecute();

        if (Connection.State != ConnectionState.Open)
        {
            OnOpeningConnection();
            await Connection.OpenAsync(cancellationToken);
        }

        var transaction = await Connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        _activeTransaction = new DbTransactionSession(
            transaction,
            TransactionCompleted,
            QueryCompiler,
            MappingProvider,
            CommandObserver);
        return _activeTransaction;
    }

    protected override void EnsureCanExecute()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transactionCleanupFailed)
        {
            throw new InvalidOperationException("Transaction cleanup failed. Dispose this session; the connection state is uncertain.");
        }
        if (_activeTransaction is not null)
        {
            throw new InvalidOperationException(
                "Use the active transaction object to execute commands until it completes.");
        }
    }

    protected override void OnOpeningConnection()
    {
        if (!_ownsConnection)
        {
            _openedBorrowedConnection = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? failure = null;
        try { _activeTransaction?.Dispose(); }
        catch (Exception error) { failure = error; }
        try
        {
            if (_ownsConnection) Connection.Dispose();
            else if (_openedBorrowedConnection && Connection.State != ConnectionState.Closed) Connection.Close();
        }
        catch (Exception error) { failure = CleanupErrors.Add(failure, error); }
        CleanupErrors.ThrowIfAny(failure);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? failure = null;
        try
        {
            if (_activeTransaction is not null) await _activeTransaction.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        try
        {
            if (_ownsConnection) await Connection.DisposeAsync().ConfigureAwait(false);
            else if (_openedBorrowedConnection && Connection.State != ConnectionState.Closed)
                await Connection.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception error) { failure = CleanupErrors.Add(failure, error); }
        CleanupErrors.ThrowIfAny(failure);
    }

    private void TransactionCompleted(DbTransactionSession transaction, bool cleanupFailed)
    {
        _transactionCleanupFailed |= cleanupFailed;
        if (ReferenceEquals(_activeTransaction, transaction))
        {
            _activeTransaction = null;
        }
    }
}
