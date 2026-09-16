using System.Data.Common;

namespace SnapData;

public sealed class DbTransactionSession : DbExecutor, IDisposable, IAsyncDisposable
{
    private readonly DbTransaction _dbTransaction;
    private readonly Action<DbTransactionSession, bool> _completed;
    private bool _finished;
    private bool _disposed;

    internal DbTransactionSession(
        DbTransaction transaction,
        Action<DbTransactionSession, bool> completed,
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

        _disposed = true;
        Exception? failure = null;
        try { if (!_finished) _dbTransaction.Rollback(); }
        catch (Exception error) { failure = error; }
        try { _dbTransaction.Dispose(); }
        catch (Exception error) { failure = CleanupErrors.Add(failure, error); }
        Finish(cleanupFailed: failure is not null);
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
        try { if (!_finished) await _dbTransaction.RollbackAsync().ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        try { await _dbTransaction.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failure = CleanupErrors.Add(failure, error); }
        Finish(cleanupFailed: failure is not null);
        CleanupErrors.ThrowIfAny(failure);
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
        {
            throw new InvalidOperationException("The transaction has already completed.");
        }
    }

    private void Finish(bool cleanupFailed = false)
    {
        if (_finished && !cleanupFailed)
        {
            return;
        }

        _finished = true;
        _completed(this, cleanupFailed);
    }
}
