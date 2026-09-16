namespace SnapData;

public abstract partial class DbExecutor
{
    public async Task<InsertManyResult> InsertManyAsync<T>(
        IEnumerable<T> entities,
        InsertManyOptions? options = null,
        QueryOptions? queryOptions = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(entities);
        options ??= new InsertManyOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BatchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxParametersPerCommand);
        EnsureCanExecute();
        cancellationToken.ThrowIfCancellationRequested();
        var iterator = entities.GetEnumerator();
        var iteratorDisposed = false;
        Exception? failure = null;
        DbTransactionSession? ownedTransaction = null;
        void DisposeIterator()
        {
            if (iteratorDisposed) return;
            iteratorDisposed = true;
            iterator.Dispose();
        }
        try
        {
            if (!iterator.MoveNext()) return new(0, 0, 0);
            var mapping = _mappingProvider.GetMapping<T>();
            var capabilities = Capabilities;
            var parameterBudget = Math.Min(options.MaxParametersPerCommand,
                capabilities.MaxParametersPerCommand ?? int.MaxValue);
            var batchSize = Math.Min(options.BatchSize, capabilities.MaxRowsPerInsert ?? int.MaxValue);
            if (!capabilities.MultiRowInsert || mapping.InsertableProperties.Count == 0) batchSize = 1;
            if (mapping.InsertableProperties.Count > 0)
                batchSize = Math.Min(batchSize, parameterBudget / mapping.InsertableProperties.Count);
            if (parameterBudget <= 0 || batchSize <= 0)
                throw new InvalidOperationException("One mapped row exceeds the configured insert parameter or row budget.");

            if (options.Atomic && Transaction is null)
            {
                if (this is not DbSession session)
                    throw new NotSupportedException("Atomic batching requires a SnapData session or an existing transaction.");
                var transaction = ownedTransaction = await session.BeginTransactionAsync(cancellationToken: cancellationToken);
                var result = await InsertManyCoreAsync(transaction, iterator, mapping, batchSize, queryOptions, cancellationToken);
                // A failing enumerator Dispose must occur before committing the owned transaction.
                DisposeIterator();
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            var unownedResult = await InsertManyCoreAsync(this, iterator, mapping, batchSize, queryOptions, cancellationToken);
            DisposeIterator();
            return unownedResult;
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            // Retain the operation failure (including cancellation); cleanup errors
            // remain inspectable without replacing its type or original stack.
            Exception? cleanupFailure = null;
            try { DisposeIterator(); }
            catch (Exception error) { cleanupFailure = error; }
            if (ownedTransaction is not null)
            {
                try { await ownedTransaction.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { cleanupFailure = CleanupErrors.Add(cleanupFailure, error); }
            }
            if (cleanupFailure is not null)
            {
                if (failure is not null) CleanupErrors.Add(failure, cleanupFailure);
                else CleanupErrors.ThrowIfAny(cleanupFailure);
            }
        }
    }

    private static async Task<InsertManyResult> InsertManyCoreAsync<T>(
        IDbExecutor executor, IEnumerator<T> iterator, EntityMapping mapping,
        int batchSize, QueryOptions? options, CancellationToken cancellationToken) where T : class
    {
        long rowsSubmitted = 0;
        long statements = 0;
        long? affectedRows = 0;
        var rows = new List<object>(Math.Min(batchSize, 1024));
        async Task FlushAsync()
        {
            if (rows.Count == 0) return;
            var query = Sql.InsertInto(mapping.Table);
            if (mapping.InsertableProperties.Count == 0) query.DefaultValues();
            else query.Rows(rows.ToArray());
            var affected = await executor.ExecuteAsync(query, options, cancellationToken).ConfigureAwait(false);
            rowsSubmitted = checked(rowsSubmitted + rows.Count);
            statements++;
            affectedRows = affectedRows is { } total && affected >= 0 ? checked(total + affected) : null;
            rows.Clear();
        }
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entity = iterator.Current;
            ArgumentNullException.ThrowIfNull(entity);
            // Snapshot values as literals: expression-valued properties cannot inflate the parameter budget.
            rows.Add(mapping.InsertableProperties.ToDictionary(property => property.ColumnName,
                property => (object?)new ParameterValueExpression(property.GetValue(entity))));
            if (rows.Count == batchSize) await FlushAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        } while (iterator.MoveNext());
        await FlushAsync().ConfigureAwait(false);
        return new(rowsSubmitted, statements, affectedRows);
    }
}
