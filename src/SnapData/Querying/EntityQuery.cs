using System.Linq.Expressions;

namespace SnapData;

public sealed class EntityQuery<T> where T : class
{
    private readonly IDbExecutor _executor;
    private readonly IEntityMappingProvider _mappingProvider;
    private EntityExpressionTranslator<T> _translator;
    private readonly SelectQueryBuilder _query;
    private readonly EntityMapping _mapping;
    private TableReference _source;
    private bool _hasExplicitProjection;
    private readonly List<IEntityRelationLoader<T>> _relationLoaders = [];

    internal EntityQuery(
        IDbExecutor executor,
        EntityMapping mapping,
        IEntityMappingProvider mappingProvider,
        TableReference? source = null)
    {
        _executor = executor;
        _mapping = mapping;
        _mappingProvider = mappingProvider;
        _source = source ?? mapping.Table;
        _translator = new EntityExpressionTranslator<T>(mapping, _source.Alias);
        _query = Sql
            .Select(
                mapping.SelectableProperties[0].Column,
                mapping.SelectableProperties.Skip(1).Select(property => property.Column).ToArray())
            .From(_source);
    }

    public EntityQuery<T> As(string alias)
    {
        _source = _source.As(alias);
        _query.From(_source);
        _translator = new EntityExpressionTranslator<T>(_mapping, alias);
        if (!_hasExplicitProjection)
        {
            SelectMappedColumns(alias);
        }

        return this;
    }

    public EntityQuery<T> Select(params string[] columns)
    {
        _query.Select(columns);
        _hasExplicitProjection = true;
        return this;
    }

    public ProjectedQuery<TResult> Select<TResult>(params string[] columns)
        where TResult : class
    {
        Select(columns);
        return new ProjectedQuery<TResult>(_executor, _query);
    }

    public EntityQuery<T> Select(
        ColumnReference column,
        params ColumnReference[] columns)
    {
        _query.Select(column, columns);
        _hasExplicitProjection = true;
        return this;
    }

    public ProjectedQuery<TResult> Select<TResult>(
        ColumnReference column,
        params ColumnReference[] columns)
        where TResult : class
    {
        Select(column, columns);
        return new ProjectedQuery<TResult>(_executor, _query);
    }

    public EntityQuery<T> Select(
        ISelectExpression expression,
        params ISelectExpression[] expressions)
    {
        _query.Select(expression, expressions);
        _hasExplicitProjection = true;
        return this;
    }

    public ProjectedQuery<TResult> Select<TResult>(
        ISelectExpression expression,
        params ISelectExpression[] expressions)
        where TResult : class
    {
        Select(expression, expressions);
        return new ProjectedQuery<TResult>(_executor, _query);
    }

    public EntityQuery<T> Distinct(bool distinct = true)
    {
        _query.Distinct(distinct);
        return this;
    }

    public EntityQuery<T> GroupBy(params string[] columns)
    {
        _query.GroupBy(columns);
        return this;
    }

    public EntityQuery<T> GroupBy<TValue>(Expression<Func<T, TValue>> property)
    {
        _query.GroupBy(_translator.TranslateProperty(property));
        return this;
    }

    public EntityQuery<T> Having(PredicateExpression predicate)
    {
        _query.Having(predicate);
        return this;
    }

    public EntityQuery<T> Having(string criteria, object? parameters = null)
    {
        _query.Having(criteria, parameters);
        return this;
    }

    public EntityQuery<T> Join(string clause, object? parameters = null)
    {
        _query.Join(clause, parameters);
        return this;
    }

    public EntityQuery<T> LeftJoin(string clause, object? parameters = null)
    {
        _query.LeftJoin(clause, parameters);
        return this;
    }

    public EntityQuery<T> RightJoin(string clause, object? parameters = null)
    {
        _query.RightJoin(clause, parameters);
        return this;
    }

    public EntityQuery<T> FullJoin(string clause, object? parameters = null)
    {
        _query.FullJoin(clause, parameters);
        return this;
    }

    public EntityQuery<T> CrossJoin(string table)
    {
        _query.CrossJoin(table);
        return this;
    }

    public EntityQuery<T> Where(Expression<Func<T, bool>> predicate)
    {
        _query.Where(_translator.Translate(predicate));
        return this;
    }

    public EntityQuery<T> Where(PredicateExpression predicate)
    {
        _query.Where(predicate);
        return this;
    }

    public EntityQuery<T> Where(string criteria, object? parameters = null)
    {
        _query.Where(criteria, parameters);
        return this;
    }

    public EntityQuery<T> OrWhere(Expression<Func<T, bool>> predicate)
    {
        _query.OrWhere(_translator.Translate(predicate));
        return this;
    }

    public EntityQuery<T> OrWhere(PredicateExpression predicate)
    {
        _query.OrWhere(predicate);
        return this;
    }

    public EntityQuery<T> OrWhere(string criteria, object? parameters = null)
    {
        _query.OrWhere(criteria, parameters);
        return this;
    }

    public EntityQuery<T> OrderBy<TValue>(Expression<Func<T, TValue>> property)
    {
        _query.OrderBy(_translator.TranslateProperty(property));
        return this;
    }

    public EntityQuery<T> OrderByDescending<TValue>(Expression<Func<T, TValue>> property)
    {
        _query.OrderByDescending(_translator.TranslateProperty(property));
        return this;
    }

    public EntityQuery<T> OrderBy(string column)
    {
        _query.OrderBy(column);
        return this;
    }

    public EntityQuery<T> OrderByDescending(string column)
    {
        _query.OrderByDescending(column);
        return this;
    }

    public EntityQuery<T> Limit(int limit)
    {
        _query.Limit(limit);
        return this;
    }

    public EntityQuery<T> Offset(int offset)
    {
        _query.Offset(offset);
        return this;
    }

    public EntityQuery<T> Include<TNavigation>(
        Expression<Func<T, TNavigation>> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        var body = navigation.Body;
        while (body is UnaryExpression unary
            && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            body = unary.Operand;
        }

        if (body is not MemberExpression member || member.Expression != navigation.Parameters[0])
        {
            throw new ArgumentException(
                "Include requires a direct navigation property expression.",
                nameof(navigation));
        }

        var relation = _mappingProvider.GetMapping<T>().FindRelation(member.Member.Name)
            ?? throw new InvalidOperationException(
                $"Property {typeof(T).Name}.{member.Member.Name} is not a mapped relation.");
        if (relation.Cardinality != RelationCardinality.Reference)
        {
            throw new NotSupportedException(
                $"Collection relation {typeof(T).Name}.{relation.NavigationName} is not supported yet.");
        }

        if (_relationLoaders.Any(loader => loader.Relation.Navigation == relation.Navigation))
        {
            return this;
        }

        var loaderType = typeof(ReferenceRelationLoader<,>).MakeGenericType(
            typeof(T),
            relation.RelatedType);
        var loader = Activator.CreateInstance(loaderType, relation, _mappingProvider)
            as IEntityRelationLoader<T>
            ?? throw new InvalidOperationException(
                $"Could not create relation loader for {typeof(T).Name}.{relation.NavigationName}.");
        _relationLoaders.Add(loader);
        return this;
    }

    public async Task<IReadOnlyList<T>> ToListAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await _executor.QueryAsync<T>(_query, options, cancellationToken);
        foreach (var loader in _relationLoaders)
        {
            await loader.LoadAsync(entities, _executor, options, cancellationToken);
        }

        return entities;
    }

    public async Task<T?> FirstOrDefaultAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _executor.QuerySingleOrDefaultAsync<T>(
            _query.Clone(1),
            options,
            cancellationToken);
        await LoadRelationsAsync(entity is null ? [] : [entity], options, cancellationToken);
        return entity;
    }

    public async Task<T> FirstAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await FirstOrDefaultAsync(options, cancellationToken)
        ?? throw new InvalidOperationException("The query returned no rows.");

    public async Task<T?> SingleOrDefaultAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _executor.QuerySingleOrDefaultAsync<T>(
            _query.Clone(2),
            options,
            cancellationToken);
        await LoadRelationsAsync(entity is null ? [] : [entity], options, cancellationToken);
        return entity;
    }

    public async Task<T> SingleAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await SingleOrDefaultAsync(options, cancellationToken)
        ?? throw new InvalidOperationException("The query returned no rows.");

    public async Task<bool> AnyAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await _executor.ScalarAsync<long>(
            new CountQueryBuilder(_query.Clone(1)),
            options,
            cancellationToken) > 0;

    public Task<long> CountAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _executor.ScalarAsync<long>(
            new CountQueryBuilder(_query.Clone()),
            options,
            cancellationToken);

    public async Task<PageResult<T>> PageAsync(
        int pageNumber,
        int pageSize,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        var offset = checked((pageNumber - 1) * pageSize);
        var totalCount = await _executor.ScalarAsync<long>(
            new CountQueryBuilder(_query.CloneWithoutPaging()),
            options,
            cancellationToken);
        var pageQuery = _query.Clone().Offset(offset).Limit(pageSize);
        var items = await _executor.QueryAsync<T>(pageQuery, options, cancellationToken);
        await LoadRelationsAsync(items, options, cancellationToken);
        return new PageResult<T>(items, totalCount, pageNumber, pageSize);
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) => _query.Build(compiler);

    private void SelectMappedColumns(string qualifier)
    {
        var columns = _mapping.SelectableProperties
            .Select(property => property.Column.Qualify(qualifier))
            .ToArray();
        _query.Select(columns[0], columns[1..]);
    }

    private async Task LoadRelationsAsync(
        IReadOnlyList<T> entities,
        QueryOptions? options,
        CancellationToken cancellationToken)
    {
        foreach (var loader in _relationLoaders)
        {
            await loader.LoadAsync(entities, _executor, options, cancellationToken);
        }
    }
}

public sealed class ProjectedQuery<TResult>
    where TResult : class
{
    private readonly IDbExecutor _executor;
    private readonly SelectQueryBuilder _query;

    internal ProjectedQuery(IDbExecutor executor, SelectQueryBuilder query)
    {
        _executor = executor;
        _query = query;
    }

    public ProjectedQuery<TResult> Distinct(bool distinct = true)
    {
        _query.Distinct(distinct);
        return this;
    }

    public ProjectedQuery<TResult> Where(PredicateExpression predicate)
    {
        _query.Where(predicate);
        return this;
    }

    public ProjectedQuery<TResult> Where(string criteria, object? parameters = null)
    {
        _query.Where(criteria, parameters);
        return this;
    }

    public ProjectedQuery<TResult> GroupBy(params string[] columns)
    {
        _query.GroupBy(columns);
        return this;
    }

    public ProjectedQuery<TResult> Having(PredicateExpression predicate)
    {
        _query.Having(predicate);
        return this;
    }

    public ProjectedQuery<TResult> Having(string criteria, object? parameters = null)
    {
        _query.Having(criteria, parameters);
        return this;
    }

    public ProjectedQuery<TResult> OrderBy(string column)
    {
        _query.OrderBy(column);
        return this;
    }

    public ProjectedQuery<TResult> OrderByDescending(string column)
    {
        _query.OrderByDescending(column);
        return this;
    }

    public ProjectedQuery<TResult> Limit(int limit)
    {
        _query.Limit(limit);
        return this;
    }

    public ProjectedQuery<TResult> Offset(int offset)
    {
        _query.Offset(offset);
        return this;
    }

    public Task<IReadOnlyList<TResult>> ToListAsync(
        QueryOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _executor.QueryAsync<TResult>(_query, options, cancellationToken);

    public async Task<PageResult<TResult>> PageAsync(
        int pageNumber,
        int pageSize,
        QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        var offset = checked((pageNumber - 1) * pageSize);
        var totalCount = await _executor.ScalarAsync<long>(
            new CountQueryBuilder(_query.CloneWithoutPaging()),
            options,
            cancellationToken);
        var items = await _executor.QueryAsync<TResult>(
            _query.Clone().Offset(offset).Limit(pageSize),
            options,
            cancellationToken);
        return new PageResult<TResult>(items, totalCount, pageNumber, pageSize);
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) => _query.Build(compiler);
}

public sealed class PageResult<T>
{
    internal PageResult(
        IReadOnlyList<T> items,
        long totalCount,
        int pageNumber,
        int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }

    public long TotalCount { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public long TotalPages => TotalCount == 0
        ? 0
        : ((TotalCount - 1) / PageSize) + 1;

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}
