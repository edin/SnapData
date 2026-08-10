namespace SnapData;

public sealed class SourceQuery
{
    private readonly IDbExecutor _executor;
    private readonly SelectQueryBuilder _query;

    internal SourceQuery(IDbExecutor executor, SelectQueryBuilder query)
    {
        _executor = executor;
        _query = query;
    }

    public SourceQuery Join(string clause, object? parameters = null)
    {
        _query.Join(clause, parameters);
        return this;
    }

    public SourceQuery LeftJoin(string clause, object? parameters = null)
    {
        _query.LeftJoin(clause, parameters);
        return this;
    }

    public SourceQuery Where(string criteria, object? parameters = null)
    {
        _query.Where(criteria, parameters);
        return this;
    }

    public SourceQuery OrderBy(string column)
    {
        _query.OrderBy(column);
        return this;
    }

    public ProjectedQuery<TResult> Select<TResult>(params string[] columns)
        where TResult : class
    {
        _query.Select(columns);
        return new ProjectedQuery<TResult>(_executor, _query);
    }

    public ProjectedQuery<TResult> Select<TResult>(
        ColumnReference column,
        params ColumnReference[] columns)
        where TResult : class
    {
        _query.Select(column, columns);
        return new ProjectedQuery<TResult>(_executor, _query);
    }

    public ProjectedQuery<TResult> Select<TResult>(
        ISelectExpression expression,
        params ISelectExpression[] expressions)
        where TResult : class
    {
        _query.Select(expression, expressions);
        return new ProjectedQuery<TResult>(_executor, _query);
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) => _query.Build(compiler);
}
