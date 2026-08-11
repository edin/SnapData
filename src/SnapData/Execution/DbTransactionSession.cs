using System.Data.Common;

namespace SnapData;

public sealed class DbTransactionSession : DbExecutor, IDisposable, IAsyncDisposable
{
    private readonly DbTransaction _dbTransaction;
    private readonly Action<DbTransactionSession> _completed;
    private bool _finished;
    private bool _disposed;

    internal DbTransactionSession(
        DbTransaction transaction,
        Action<DbTransactionSession> completed,
        IQueryCompiler queryCompiler,
        IEntityMappingProvider mappingProvider,
        ICommandObserver? commandObserver)
        : base(transaction.Connection
            ?? throw new InvalidOperationException("The transaction has no connection."), transaction, queryCompiler, mappingProvider, commandObserver)
    {
        _dbTransaction = transaction;
        _completed = completed;
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await _dbTransaction.CommitAsync(cancellationToken);
        Finish();
    }

    public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await _dbTransaction.RollbackAsync(cancellationToken);
        Finish();
    }

    protected override void EnsureCanExecute() => EnsureActive();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_finished)
        {
            _dbTransaction.Rollback();
            Finish();
        }

        _dbTransaction.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (!_finished)
        {
            await _dbTransaction.RollbackAsync();
            Finish();
        }

        await _dbTransaction.DisposeAsync();
        _disposed = true;
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
        {
            throw new InvalidOperationException("The transaction has already completed.");
        }
    }

    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _completed(this);
    }
}
