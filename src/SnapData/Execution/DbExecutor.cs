using System.Data;
using System.Data.Common;
using System.Collections;
using System.Reflection;

namespace SnapData;

public abstract class DbExecutor : IDbExecutor
{
    private static readonly MethodInfo ReadProcedureResultSetMethod = typeof(DbExecutor)
        .GetMethod(nameof(ReadProcedureResultSetAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly DbConnection _connection;
    private readonly DbTransaction? _transaction;
    private readonly IQueryCompiler _queryCompiler;
    private readonly IEntityMappingProvider _mappingProvider;
    private readonly IEntityCommandFactory _entityCommands;
    private readonly ICommandObserver? _commandObserver;

    protected DbExecutor(
        DbConnection connection,
        DbTransaction? transaction = null,
        IQueryCompiler? queryCompiler = null,
        IEntityMappingProvider? mappingProvider = null,
        ICommandObserver? commandObserver = null)
    {
        _connection = connection;
        _transaction = transaction;
        _queryCompiler = queryCompiler ?? SqlDialect.Ansi;
        _mappingProvider = mappingProvider ?? EntityMappingProvider.Default;
        _entityCommands = new EntityCommandFactory(_mappingProvider);
        _commandObserver = commandObserver;
    }

    protected DbConnection Connection => _connection;

    protected DbTransaction? Transaction => _transaction;

    protected IQueryCompiler QueryCompiler => _queryCompiler;

    protected IEntityMappingProvider MappingProvider => _mappingProvider;

    protected ICommandObserver? CommandObserver => _commandObserver;

    protected virtual void EnsureCanExecute()
    {
    }

    protected virtual void OnOpeningConnection()
    {
    }

    public EntityReference<T> Entity<T>(string? alias = null) where T : class =>
        new(_mappingProvider.GetMapping<T>(), alias);

    public EntityQuery<T> From<T>() where T : class =>
        new(this, _mappingProvider.GetMapping<T>(), _mappingProvider);

    public EntityQuery<T> From<T>(string alias) where T : class =>
        new(
            this,
            _mappingProvider.GetMapping<T>(),
            _mappingProvider,
            _mappingProvider.GetMapping<T>().Table.As(alias));

    public EntityQuery<T> From<T>(TableReference source) where T : class =>
        new(this, _mappingProvider.GetMapping<T>(), _mappingProvider, source);

    public EntityQuery<T> From<T>(EntityReference<T> source) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        return From<T>(source.Table);
    }

    public SourceQuery From(string source) => new(this, Sql.From(source));

    public SourceQuery From(TableReference source) => new(this, Sql.From(source));

    public EntityInsert<T> InsertInto<T>() where T : class =>
        new(this, _mappingProvider.GetMapping<T>());

    public EntityUpdate<T> Update<T>() where T : class =>
        new(this, _mappingProvider.GetMapping<T>());

    public EntityDelete<T> DeleteFrom<T>() where T : class =>
        new(this, _mappingProvider.GetMapping<T>());

    public Task<TResult> Query<TResult>(
        IStoredProc<TResult> procedure,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        QueryProcedureAsync(procedure, options, cancellationToken);

    public async Task<TResult> QueryProcedureAsync<TResult>(
        IStoredProc<TResult> procedure,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        var definition = StoredProcedureCommandFactory.Create(procedure);
        var mapping = StoredProcedureResultMappingProvider.Get<TResult>();
        var result = mapping.CreateInstance();
        await using var command = await CreateCommandAsync(
            definition,
            options,
            cancellationToken);
        var observation = Observe(definition, CommandExecutionKind.Query);
        var resultCount = 0;
        try
        {
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                foreach (var resultSet in mapping.ResultSets)
                {
                    if (resultSet.Index > 0
                        && !await reader.NextResultAsync(cancellationToken))
                    {
                        throw new InvalidOperationException(
                            $"Stored procedure {definition.CommandText} did not return result set {resultSet.Index} expected by {typeof(TResult).Name}.{resultSet.Property.Name}.");
                    }

                    var items = await ReadProcedureResultSet(
                        resultSet.ItemType,
                        reader,
                        cancellationToken);
                    resultCount += items.Count;
                    var target = resultSet.Property.GetValue(result) as IList;
                    if (target is null)
                    {
                        if (!resultSet.Property.CanWrite)
                        {
                            throw new InvalidOperationException(
                                $"Result-set property {typeof(TResult).Name}.{resultSet.Property.Name} must return a non-null List<{resultSet.ItemType.Name}> or have a setter.");
                        }

                        target = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(resultSet.ItemType))!;
                        resultSet.Property.SetValue(result, target);
                    }

                    foreach (var item in items)
                    {
                        target.Add(item);
                    }
                }
            }

            definition.Parameters.CaptureOutput(command.Parameters);
            StoredProcedureCommandFactory.ApplyOutputs(procedure, definition.Parameters);
            observation?.Complete(resultCount: resultCount);
            return (TResult)result;
        }
        catch (Exception exception)
        {
            observation?.Fail(exception);
            throw;
        }
    }

    private async Task<IList> ReadProcedureResultSet(
        Type itemType,
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        var method = ReadProcedureResultSetMethod.MakeGenericMethod(itemType);
        var task = (Task<IList>)method.Invoke(this, [reader, cancellationToken])!;
        return await task;
    }

    private async Task<IList> ReadProcedureResultSetAsync<T>(
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        IList items = new List<T>();
        var map = RowMapper<T>.Create(reader, _mappingProvider);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(map.Map(reader));
        }

        return items;
    }

    public async Task<int> InsertAsync<T>(
        T entity,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        var insert = _entityCommands.Insert(entity);
        var generated = _mappingProvider
            .GetMapping(entity.GetType())
            .Properties
            .Where(property => property.IsGenerated && property.CanWrite)
            .ToArray();

        if (generated.Length == 0)
        {
            return await ExecuteAsync(insert, options, cancellationToken);
        }

        var plan = _queryCompiler.CompileGeneratedInsert(
            insert,
            generated.Select(property => property.Column).ToArray());
        if (plan.FollowUpQuery is not null)
        {
            var affected = await ExecuteAsync(plan.Command, options, cancellationToken);
            var value = await ScalarAsync<object>(
                plan.FollowUpQuery,
                options,
                cancellationToken);
            generated[0].SetValue(
                entity,
                RowMapper<T>.ConvertValue(value, generated[0].PropertyType));
            return affected;
        }

        var definition = plan.Command;
        await using var command = await CreateCommandAsync(
            definition,
            options,
            cancellationToken);
        var observation = Observe(definition, CommandExecutionKind.NonQuery);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow,
                cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Insert for entity {entity.GetType().Name} did not return generated values.");
            }

            for (var index = 0; index < generated.Length; index++)
            {
                var value = reader.IsDBNull(index)
                    ? null
                    : RowMapper<T>.ConvertValue(reader.GetValue(index), generated[index].PropertyType);
                generated[index].SetValue(entity, value);
            }

            observation?.Complete(affectedRows: 1);
            return 1;
        }
        catch (Exception exception)
        {
            observation?.Fail(exception);
            throw;
        }
    }

    public Task<int> UpdateAsync<T>(
        T entity,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class =>
        ExecuteAsync(_entityCommands.Update(entity), options, cancellationToken);

    public Task<int> DeleteAsync<T>(
        T entity,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class =>
        ExecuteAsync(_entityCommands.Delete(entity), options, cancellationToken);

    public Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(CreateQuery(sql, parameters), options, cancellationToken);

    public async Task<int> ExecuteAsync(
        SqlQuery query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync((CommandDefinition)query, options, cancellationToken);

    public async Task<int> ExecuteAsync(
        CommandDefinition commandDefinition,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = await CreateCommandAsync(
            commandDefinition,
            options,
            cancellationToken);
        var observation = Observe(commandDefinition, CommandExecutionKind.NonQuery);
        try
        {
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            commandDefinition.Parameters.CaptureOutput(command.Parameters);
            observation?.Complete(affectedRows: affected);
            return affected;
        }
        catch (Exception exception)
        {
            observation?.Fail(exception);
            throw;
        }
    }

    public Task<int> ExecuteAsync(
        ISqlQueryBuilder query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(Build(query), options, cancellationToken);

    public Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync<T>(CreateQuery(sql, parameters), options, cancellationToken);

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlQuery query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await QueryAsync<T>((CommandDefinition)query, options, cancellationToken);

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        CommandDefinition commandDefinition,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = await CreateCommandAsync(
            commandDefinition,
            options,
            cancellationToken);
        var observation = Observe(commandDefinition, CommandExecutionKind.Query);
        try
        {
            var results = new List<T>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                var map = RowMapper<T>.Create(reader, _mappingProvider);
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(map.Map(reader));
                }
            }

            commandDefinition.Parameters.CaptureOutput(command.Parameters);
            observation?.Complete(resultCount: results.Count);
            return results;
        }
        catch (Exception exception)
        {
            observation?.Fail(exception);
            throw;
        }
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(
        ISqlQueryBuilder query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        QueryAsync<T>(Build(query), options, cancellationToken);

    public Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        QuerySingleOrDefaultAsync<T>(CreateQuery(sql, parameters), options, cancellationToken);

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        SqlQuery query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await QuerySingleOrDefaultAsync<T>(
            (CommandDefinition)query,
            options,
            cancellationToken);

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        CommandDefinition commandDefinition,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = await CreateCommandAsync(
            commandDefinition,
            options,
            cancellationToken);
        var observation = Observe(commandDefinition, CommandExecutionKind.Query);
        try
        {
            T? result = default;
            var resultCount = 0;
            await using (var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleResult,
                cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    resultCount = 1;
                    var map = RowMapper<T>.Create(reader, _mappingProvider);
                    result = map.Map(reader);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        throw new InvalidOperationException("The query returned more than one row.");
                    }
                }
            }

            commandDefinition.Parameters.CaptureOutput(command.Parameters);
            observation?.Complete(resultCount: resultCount);
            return result;
        }
        catch (Exception exception)
        {
            observation?.Fail(exception);
            throw;
        }
    }

    public Task<T?> QuerySingleOrDefaultAsync<T>(
        ISqlQueryBuilder query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        QuerySingleOrDefaultAsync<T>(Build(query), options, cancellationToken);

    public Task<T> ScalarAsync<T>(
        string sql,
        object? parameters = null,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ScalarAsync<T>(CreateQuery(sql, parameters), options, cancellationToken);

    public async Task<T> ScalarAsync<T>(
        SqlQuery query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await ScalarAsync<T>((CommandDefinition)query, options, cancellationToken);

    public async Task<T> ScalarAsync<T>(
        CommandDefinition commandDefinition,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = await CreateCommandAsync(
            commandDefinition,
            options,
            cancellationToken);
        var observation = Observe(commandDefinition, CommandExecutionKind.Scalar);
        try
        {
            var value = await command.ExecuteScalarAsync(cancellationToken);
            commandDefinition.Parameters.CaptureOutput(command.Parameters);
            observation?.Complete(resultCount: value is null or DBNull ? 0 : 1);

            if (value is null || value is DBNull)
            {
                return default!;
            }

            return (T)RowMapper<T>.ConvertValue(value, typeof(T))!;
        }
        catch (Exception exception)
        {
            observation?.Fail(exception);
            throw;
        }
    }

    public Task<T> ScalarAsync<T>(
        ISqlQueryBuilder query,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ScalarAsync<T>(Build(query), options, cancellationToken);

    private async ValueTask<DbCommand> CreateCommandAsync(
        CommandDefinition commandDefinition,
        QueryOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandDefinition);
        EnsureCanExecute();

        if (_connection.State != ConnectionState.Open)
        {
            OnOpeningConnection();
            await _connection.OpenAsync(cancellationToken);
        }

        var command = _connection.CreateCommand();
        command.CommandText = commandDefinition.CommandText;
        command.CommandType = commandDefinition.CommandType;
        command.Transaction = _transaction;

        if (options?.CommandTimeout is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        foreach (var definition in commandDefinition.Parameters.Definitions)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = definition.Name.StartsWith('@')
                ? definition.Name
                : $"@{definition.Name}";
            parameter.Value = definition.Value ?? DBNull.Value;
            parameter.Direction = definition.Direction;
            if (definition.DbType is { } dbType)
            {
                parameter.DbType = dbType;
            }

            if (definition.Size is { } size)
            {
                parameter.Size = size;
            }

            if (definition.Precision is { } precision)
            {
                parameter.Precision = precision;
            }

            if (definition.Scale is { } scale)
            {
                parameter.Scale = scale;
            }

            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static SqlQuery CreateQuery(string sql, object? parameters) =>
        new(sql, ParameterSet.From(parameters));

    private CommandObservation? Observe(
        CommandDefinition definition,
        CommandExecutionKind kind) =>
        CommandObservation.Start(
            _commandObserver,
            definition,
            kind,
            _connection.GetType().Name,
            _transaction is not null);

    private SqlQuery Build(ISqlQueryBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query is CountQueryBuilder count)
        {
            var inner = _queryCompiler.Compile(count.Query);
            return new SqlQuery(
                $"SELECT COUNT(*) FROM ({inner.Text}) AS snap_count",
                inner.Parameters);
        }

        return _queryCompiler.Compile(query);
    }
}
