using System.Linq.Expressions;
using System.Numerics;

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
        if (_mapping.InsertableProperties.Count == 0)
        {
            _query.DefaultValues();
        }
        else
        {
            _query.Values(_mapping.InsertableProperties.ToDictionary(
                property => property.ColumnName, property => property.GetValue(entity)));
        }

        return this;
    }

    public EntityInsert<T> DefaultValues()
    {
        _query.DefaultValues();
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

    public MutationResult<TResult> Returning<TResult>(params Expression<Func<T, object?>>[] properties)
    {
        var columns = MutationReturning.Columns(_translator, properties);
        _query.Returning(columns[0], columns.Skip(1).ToArray());
        return new(_executor, _query);
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) =>
        compiler is null ? _executor.Compile(_query) : _query.Build(compiler);

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
        EnsureUpdatable(column);
        _query.Set(column, value);
        return this;
    }

    /// <summary>Assign a SQL expression without evaluating it in application code.</summary>
    public EntityUpdate<T> SetExpression<TValue>(
        Expression<Func<T, TValue>> property,
        ValueExpression value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var column = _translator.TranslateProperty(property);
        EnsureUpdatable(column);
        _query.Set(column, value);
        return this;
    }

    /// <summary>Atomically add one to a mapped numeric column in the database.</summary>
    public EntityUpdate<T> Increment<TNumber>(Expression<Func<T, TNumber>> property)
        where TNumber : INumber<TNumber> => Increment(property, TNumber.One);

    /// <summary>Atomically add a parameterized amount to a mapped numeric column.</summary>
    public EntityUpdate<T> Increment<TNumber>(Expression<Func<T, TNumber>> property, TNumber amount)
        where TNumber : INumber<TNumber> =>
        SetExpression(property, Exp.Col(_translator.TranslateProperty(property)) + amount);

    private void EnsureUpdatable(ColumnReference column)
    {
        if (!_mapping.UpdatableProperties.Any(candidate => string.Equals(
            candidate.ColumnName,
            column.Name,
            StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Column '{column.Name}' is not updatable.");
        }
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

    public MutationResult<TResult> Returning<TResult>(params Expression<Func<T, object?>>[] properties)
    {
        var columns = MutationReturning.Columns(_translator, properties);
        _query.Returning(columns[0], columns.Skip(1).ToArray());
        return new(_executor, _query);
    }

    public Task<int> ExecuteAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(_query, options, cancellationToken);

    public SqlQuery Build(IQueryCompiler? compiler = null) =>
        compiler is null ? _executor.Compile(_query) : _query.Build(compiler);
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

    public MutationResult<TResult> Returning<TResult>(params Expression<Func<T, object?>>[] properties)
    {
        var columns = MutationReturning.Columns(_translator, properties);
        _query.Returning(columns[0], columns.Skip(1).ToArray());
        return new(_executor, _query);
    }

    public Task<int> ExecuteAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(_query, options, cancellationToken);

    public SqlQuery Build(IQueryCompiler? compiler = null) =>
        compiler is null ? _executor.Compile(_query) : _query.Build(compiler);
}
