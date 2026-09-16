using System.Linq.Expressions;

namespace SnapData;

/// <summary>Mapped single-row upsert. Execution does not hydrate generated entity values.</summary>
public sealed class EntityUpsert<T> where T : class
{
    private readonly IDbExecutor executor;
    private readonly EntityMapping mapping;
    private readonly EntityExpressionTranslator<T> translator;
    private readonly UpsertQueryBuilder query;

    internal EntityUpsert(IDbExecutor executor, EntityMapping mapping)
    {
        this.executor = executor;
        this.mapping = mapping;
        translator = new(mapping);
        query = Sql.UpsertInto(mapping.Table);
    }

    public EntityUpsert<T> Values(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        query.Values(mapping.InsertableProperties.ToDictionary(property => property.ColumnName, property => property.GetValue(entity)));
        return this;
    }

    public EntityUpsert<T> Value<TValue>(Expression<Func<T, TValue>> property, TValue value)
    {
        var column = translator.TranslateProperty(property);
        EnsureAllowed(column, mapping.InsertableProperties);
        query.Value(column.Name, value);
        return this;
    }

    public EntityUpsert<T> OnConflict(params Expression<Func<T, object?>>[] properties)
    {
        query.OnConflict(Columns(properties, mapping.InsertableProperties));
        return this;
    }

    public EntityUpsert<T> UpdateColumns(params Expression<Func<T, object?>>[] properties)
    {
        query.UpdateColumns(Columns(properties, mapping.UpdatableProperties));
        return this;
    }

    public EntityUpsert<T> DoNothing()
    {
        query.DoNothing();
        return this;
    }

    public Task<int> ExecuteAsync(QueryOptions? options = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(query, options, cancellationToken);

    public MutationResult<TResult> Returning<TResult>(params Expression<Func<T, object?>>[] properties)
    {
        var columns = MutationReturning.Columns(translator, properties);
        query.Returning(columns.Select(column => column.Name).ToArray());
        return new(executor, query);
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) =>
        compiler is null ? executor.Compile(query) : query.Build(compiler);

    private string[] Columns(Expression<Func<T, object?>>[] properties, IReadOnlyList<PropertyMapping> allowed)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Select(property =>
        {
            var column = translator.TranslateProperty(property);
            EnsureAllowed(column, allowed);
            return column.Name;
        }).ToArray();
    }

    private static void EnsureAllowed(ColumnReference column, IReadOnlyList<PropertyMapping> allowed)
    {
        if (!allowed.Any(property => string.Equals(property.ColumnName, column.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Column '{column.Name}' is not writable for this upsert operation.");
    }
}
