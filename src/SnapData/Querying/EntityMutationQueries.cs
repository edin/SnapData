using System.Linq.Expressions;

namespace SnapData;

public sealed class EntityInsert<T> where T : class
{
    private readonly IDbExecutor _executor;
    private readonly EntityMapping _mapping;
    private readonly EntityExpressionTranslator<T> _translator;
    private readonly InsertQueryBuilder _query;

    internal EntityInsert(IDbExecutor executor, EntityMapping mapping)
    {
        _executor = executor;
        _mapping = mapping;
        _translator = new EntityExpressionTranslator<T>(mapping);
        _query = Sql.InsertInto(mapping.Table);
    }

    public EntityInsert<T> Values(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var property in _mapping.InsertableProperties)
        {
            _query.Value(property.Column, property.GetValue(entity));
        }

        return this;
    }

    public EntityInsert<T> Value<TValue>(
        Expression<Func<T, TValue>> property,
        TValue value)
    {
        var column = _translator.TranslateProperty(property);
        EnsureAllowed(column, _mapping.InsertableProperties, "insertable");
        _query.Value(column, value);
        return this;
    }

    public Task<int> ExecuteAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(_query, options, cancellationToken);

    public SqlQuery Build(IQueryCompiler? compiler = null) => _query.Build(compiler);

    private static void EnsureAllowed(
        ColumnReference column,
        IReadOnlyList<PropertyMapping> properties,
        string operation)
    {
        if (!properties.Any(property => string.Equals(
            property.ColumnName,
            column.Name,
            StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Column '{column.Name}' is not {operation}.");
        }
    }
}

public sealed class EntityUpdate<T> where T : class
{
    private readonly IDbExecutor _executor;
    private readonly EntityMapping _mapping;
    private readonly EntityExpressionTranslator<T> _translator;
    private readonly UpdateQueryBuilder _query;

    internal EntityUpdate(IDbExecutor executor, EntityMapping mapping)
    {
        _executor = executor;
        _mapping = mapping;
        _translator = new EntityExpressionTranslator<T>(mapping);
        _query = Sql.Update(mapping.Table);
    }

    public EntityUpdate<T> Set<TValue>(
        Expression<Func<T, TValue>> property,
        TValue value)
    {
        var column = _translator.TranslateProperty(property);
        if (!_mapping.UpdatableProperties.Any(candidate => string.Equals(
            candidate.ColumnName,
            column.Name,
            StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Column '{column.Name}' is not updatable.");
        }

        _query.Set(column, value);
        return this;
    }

    public EntityUpdate<T> Where(Expression<Func<T, bool>> predicate)
    {
        _query.Where(_translator.Translate(predicate));
        return this;
    }

    public EntityUpdate<T> Where(PredicateExpression predicate)
    {
        _query.Where(predicate);
        return this;
    }

    public EntityUpdate<T> Where(string criteria, object? parameters = null)
    {
        _query.Where(criteria, parameters);
        return this;
    }

    public EntityUpdate<T> AllRows()
    {
        _query.AllRows();
        return this;
    }

    public Task<int> ExecuteAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(_query, options, cancellationToken);

    public SqlQuery Build(IQueryCompiler? compiler = null) => _query.Build(compiler);
}

public sealed class EntityDelete<T> where T : class
{
    private readonly IDbExecutor _executor;
    private readonly EntityExpressionTranslator<T> _translator;
    private readonly DeleteQueryBuilder _query;

    internal EntityDelete(IDbExecutor executor, EntityMapping mapping)
    {
        _executor = executor;
        _translator = new EntityExpressionTranslator<T>(mapping);
        _query = Sql.DeleteFrom(mapping.Table);
    }

    public EntityDelete<T> Where(Expression<Func<T, bool>> predicate)
    {
        _query.Where(_translator.Translate(predicate));
        return this;
    }

    public EntityDelete<T> Where(PredicateExpression predicate)
    {
        _query.Where(predicate);
        return this;
    }

    public EntityDelete<T> Where(string criteria, object? parameters = null)
    {
        _query.Where(criteria, parameters);
        return this;
    }

    public EntityDelete<T> AllRows()
    {
        _query.AllRows();
        return this;
    }

    public Task<int> ExecuteAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(_query, options, cancellationToken);

    public SqlQuery Build(IQueryCompiler? compiler = null) => _query.Build(compiler);
}
