namespace SnapData.Schema;

public abstract class SchemaInspector : ISchemaInspector
{
    private readonly SnapDatabase? database;
    private readonly IDbExecutor? executor;

    protected SchemaInspector(SnapDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
    }

    protected SchemaInspector(IDbExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        this.executor = executor;
    }

    public abstract Task<bool> TableExistsAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken = default);

    public abstract Task<bool> ColumnExistsAsync(
        SchemaObjectName table,
        string column,
        CancellationToken cancellationToken = default);

    public abstract Task<IReadOnlyList<SchemaObjectInfo>> GetObjectsAsync(
        string? schema = null,
        bool includeSystemObjects = false,
        CancellationToken cancellationToken = default);

    public abstract Task<TableSchema?> GetTableAsync(
        SchemaObjectName table,
        SchemaReadOptions? options = null,
        CancellationToken cancellationToken = default);

    public abstract Task<DatabaseSchema> ReadAsync(
        SchemaReadOptions? options = null,
        CancellationToken cancellationToken = default);

    protected async Task<T> ScalarAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (executor is not null)
        {
            return await executor.ScalarAsync<T>(
                sql,
                parameters,
                cancellationToken: cancellationToken);
        }

        await using var session = await database!.OpenSessionAsync(cancellationToken);
        return await session.ScalarAsync<T>(
            sql,
            parameters,
            cancellationToken: cancellationToken);
    }

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (executor is not null)
        {
            return await executor.QueryAsync<T>(
                sql,
                parameters,
                cancellationToken: cancellationToken);
        }

        await using var session = await database!.OpenSessionAsync(cancellationToken);
        return await session.QueryAsync<T>(
            sql,
            parameters,
            cancellationToken: cancellationToken);
    }
}
