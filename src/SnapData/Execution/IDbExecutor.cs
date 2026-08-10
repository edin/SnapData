namespace SnapData;

public interface IDbExecutor
{
    EntityQuery<T> From<T>() where T : class;

    EntityQuery<T> From<T>(string source) where T : class;

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
