using System.Linq.Expressions;

namespace SnapData;

/// <summary>Executes a mutation once per terminal call and maps its returned rows.</summary>
/// <remarks>Cardinality or mapping errors occur after the database mutation. Use an explicit
/// transaction when those errors must roll back the write. Input entities are not hydrated.</remarks>
public sealed class MutationResult<TResult>
{
    private readonly IDbExecutor executor;
    private readonly ISqlQueryBuilder query;

    internal MutationResult(IDbExecutor executor, ISqlQueryBuilder query)
    {
        this.executor = executor;
        this.query = query;
    }

    public Task<IReadOnlyList<TResult>> ToListAsync(QueryOptions? options = null, CancellationToken cancellationToken = default) =>
        executor.QueryAsync<TResult>(query, options, cancellationToken);

    /// <summary>Returns default when the provider returns no rows; use a nullable result type to distinguish a missing scalar.</summary>
    public Task<TResult?> SingleOrDefaultAsync(QueryOptions? options = null, CancellationToken cancellationToken = default) =>
        executor.QuerySingleOrDefaultAsync<TResult>(query, options, cancellationToken);

    public async Task<TResult> SingleAsync(QueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var rows = await ToListAsync(options, cancellationToken).ConfigureAwait(false);
        return rows.Count == 1 ? rows[0] : throw new InvalidOperationException(
            $"The mutation returned {rows.Count} rows; exactly one was required.");
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) =>
        compiler is null ? executor.Compile(query) : query.Build(compiler);
}

internal static class MutationReturning
{
    internal static ColumnReference[] Columns<T>(EntityExpressionTranslator<T> translator,
        Expression<Func<T, object?>>[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Length == 0)
            throw new ArgumentException("Select at least one returning property.", nameof(properties));
        var columns = properties.Select(translator.TranslateProperty).ToArray();
        if (columns.Select(column => column.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Length)
            throw new ArgumentException("Duplicate returning properties are not allowed.", nameof(properties));
        return columns;
    }
}
