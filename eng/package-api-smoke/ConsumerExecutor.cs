using SnapData;

// Implements the pre-0.4 interface surface without depending on internal constructors.
// New optional operations deliberately use the interface defaults.
sealed class ConsumerExecutor(IDbExecutor inner) : IDbExecutor
{
    public EntityReference<T> Entity<T>(string? alias = null) where T : class => inner.Entity<T>(alias);
    public EntityQuery<T> From<T>() where T : class => inner.From<T>();
    public EntityQuery<T> From<T>(string alias) where T : class => inner.From<T>(alias);
    public EntityQuery<T> From<T>(TableReference source) where T : class => inner.From<T>(source);
    public EntityQuery<T> From<T>(EntityReference<T> source) where T : class => inner.From(source);
    public SourceQuery From(string source) => inner.From(source);
    public SourceQuery From(TableReference source) => inner.From(source);
    public EntityInsert<T> InsertInto<T>() where T : class => inner.InsertInto<T>();
    public EntityUpdate<T> Update<T>() where T : class => inner.Update<T>();
    public EntityDelete<T> DeleteFrom<T>() where T : class => inner.DeleteFrom<T>();
    public Task<TResult> Query<TResult>(IStoredProc<TResult> procedure, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.Query(procedure, options, cancellationToken);
    public Task<TResult> QueryProcedureAsync<TResult>(IStoredProc<TResult> procedure, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QueryProcedureAsync(procedure, options, cancellationToken);
    public Task<int> InsertAsync<T>(T entity, QueryOptions? options = null, CancellationToken cancellationToken = default) where T : class => inner.InsertAsync(entity, options, cancellationToken);
    public Task<int> UpdateAsync<T>(T entity, QueryOptions? options = null, CancellationToken cancellationToken = default) where T : class => inner.UpdateAsync(entity, options, cancellationToken);
    public Task<int> DeleteAsync<T>(T entity, QueryOptions? options = null, CancellationToken cancellationToken = default) where T : class => inner.DeleteAsync(entity, options, cancellationToken);
    public Task<int> ExecuteAsync(string sql, object? parameters = null, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.ExecuteAsync(sql, parameters, options, cancellationToken);
    public Task<int> ExecuteAsync(CommandDefinition command, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.ExecuteAsync(command, options, cancellationToken);
    public Task<int> ExecuteAsync(SqlQuery query, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.ExecuteAsync(query, options, cancellationToken);
    public Task<int> ExecuteAsync(ISqlQueryBuilder query, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.ExecuteAsync(query, options, cancellationToken);
    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QueryAsync<T>(sql, parameters, options, cancellationToken);
    public Task<IReadOnlyList<T>> QueryAsync<T>(CommandDefinition command, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QueryAsync<T>(command, options, cancellationToken);
    public Task<IReadOnlyList<T>> QueryAsync<T>(SqlQuery query, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QueryAsync<T>(query, options, cancellationToken);
    public Task<IReadOnlyList<T>> QueryAsync<T>(ISqlQueryBuilder query, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QueryAsync<T>(query, options, cancellationToken);
    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QuerySingleOrDefaultAsync<T>(sql, parameters, options, cancellationToken);
    public Task<T?> QuerySingleOrDefaultAsync<T>(CommandDefinition command, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QuerySingleOrDefaultAsync<T>(command, options, cancellationToken);
    public Task<T?> QuerySingleOrDefaultAsync<T>(SqlQuery query, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QuerySingleOrDefaultAsync<T>(query, options, cancellationToken);
    public Task<T?> QuerySingleOrDefaultAsync<T>(ISqlQueryBuilder query, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.QuerySingleOrDefaultAsync<T>(query, options, cancellationToken);
    public Task<T> ScalarAsync<T>(string sql, object? parameters = null, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.ScalarAsync<T>(sql, parameters, options, cancellationToken);
    public Task<T> ScalarAsync<T>(CommandDefinition command, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.ScalarAsync<T>(command, options, cancellationToken);
    public Task<T> ScalarAsync<T>(SqlQuery query, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.ScalarAsync<T>(query, options, cancellationToken);
    public Task<T> ScalarAsync<T>(ISqlQueryBuilder query, QueryOptions? options = null, CancellationToken cancellationToken = default) => inner.ScalarAsync<T>(query, options, cancellationToken);
}
