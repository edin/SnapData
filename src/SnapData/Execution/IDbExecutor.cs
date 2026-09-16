namespace SnapData;

public interface IDbExecutor
{
    QueryCapabilities Capabilities => QueryCapabilities.None;

    /// <summary>Compile for this executor without opening a connection or executing SQL.</summary>
    SqlQuery Compile(ISqlQueryBuilder query) =>
        throw new NotSupportedException("This custom executor does not expose provider-bound compilation. Supply an explicit compiler to Build().");

    EntityReference<T> Entity<T>(string? alias = null) where T : class;

    EntityQuery<T> From<T>() where T : class;

    EntityQuery<T> From<T>(string alias) where T : class;

    EntityQuery<T> From<T>(TableReference source) where T : class;

    EntityQuery<T> From<T>(EntityReference<T> source) where T : class;

    SourceQuery From(string source);

    SourceQuery From(TableReference source);

    EntityInsert<T> InsertInto<T>() where T : class;

    EntityUpsert<T> UpsertInto<T>() where T : class =>
        throw new NotSupportedException("This custom executor does not implement typed upserts.");

    EntityUpdate<T> Update<T>() where T : class;

    EntityDelete<T> DeleteFrom<T>() where T : class;

    Task<TResult> Query<TResult>(
        IStoredProc<TResult> procedure,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<TResult> QueryProcedureAsync<TResult>(
        IStoredProc<TResult> procedure,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<int> InsertAsync<T>(
        T entity,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<InsertManyResult> InsertManyAsync<T>(
        IEnumerable<T> entities,
        InsertManyOptions? options = null,
        QueryOptions? queryOptions = null,
        CancellationToken cancellationToken = default) where T : class =>
        throw new NotSupportedException("This custom executor does not implement mapped batched inserts.");

    Task<int> UpdateAsync<T>(
        T entity,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<int> DeleteAsync<T>(
        T entity,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteAsync(
        CommandDefinition command,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteAsync(
        SqlQuery query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteAsync(
        ISqlQueryBuilder query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        CommandDefinition command,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlQuery query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        ISqlQueryBuilder query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        CommandDefinition command,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        SqlQuery query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        ISqlQueryBuilder query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T> ScalarAsync<T>(
        string sql,
        object? parameters = null,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T> ScalarAsync<T>(
        CommandDefinition command,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T> ScalarAsync<T>(
        SqlQuery query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<T> ScalarAsync<T>(
        ISqlQueryBuilder query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default);
}
