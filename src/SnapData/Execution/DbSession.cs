using System.Data;
using System.Data.Common;

namespace SnapData;

public sealed class DbSession : DbExecutor, IDisposable, IAsyncDisposable
{
    private readonly bool _ownsConnection;
    private bool _openedBorrowedConnection;
    private DbTransactionSession? _activeTransaction;
    private bool _disposed;

    private DbSession(
        DbConnection connection,
        bool ownsConnection,
        bool openedBorrowedConnection,
        IQueryCompiler? queryCompiler,
        IEntityMappingProvider? mappingProvider)
        : base(connection, queryCompiler: queryCompiler, mappingProvider: mappingProvider)
    {
        _ownsConnection = ownsConnection;
        _openedBorrowedConnection = openedBorrowedConnection;
    }

    public static DbSession Borrow(
        DbConnection connection,
        IQueryCompiler? queryCompiler = null,
        IEntityMappingProvider? mappingProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new DbSession(
            connection,
            ownsConnection: false,
            openedBorrowedConnection: false,
            queryCompiler,
            mappingProvider);
    }

    internal static DbSession Own(
        DbConnection connection,
        IQueryCompiler queryCompiler,
        IEntityMappingProvider mappingProvider) =>
        new(
            connection,
            ownsConnection: true,
            openedBorrowedConnection: false,
            queryCompiler,
            mappingProvider);

    public async ValueTask<DbTransactionSession> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeTransaction is not null)
        {
            throw new InvalidOperationException("This session already has an active transaction.");
        }

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
            MappingProvider);
        return _activeTransaction;
    }

    protected override void EnsureCanExecute()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

        _activeTransaction?.Dispose();
        if (_ownsConnection)
        {
            Connection.Dispose();
        }
        else if (_openedBorrowedConnection && Connection.State != ConnectionState.Closed)
        {
            Connection.Close();
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_activeTransaction is not null)
        {
            await _activeTransaction.DisposeAsync();
        }

        if (_ownsConnection)
        {
            await Connection.DisposeAsync();
        }
        else if (_openedBorrowedConnection && Connection.State != ConnectionState.Closed)
        {
            await Connection.CloseAsync();
        }

        _disposed = true;
    }

    private void TransactionCompleted(DbTransactionSession transaction)
    {
        if (ReferenceEquals(_activeTransaction, transaction))
        {
            _activeTransaction = null;
        }
    }
}
