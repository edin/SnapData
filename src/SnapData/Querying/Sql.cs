namespace SnapData;

public static class Sql
{
    public static AggregateExpression Count(string column = "*") =>
        new(AggregateFunction.Count, ColumnReference.Parse(column));

    public static AggregateExpression Count(ColumnReference column) =>
        new(AggregateFunction.Count, column);

    public static AggregateExpression Sum(string column) =>
        new(AggregateFunction.Sum, ColumnReference.Parse(column));

    public static AggregateExpression Sum(ColumnReference column) =>
        new(AggregateFunction.Sum, column);

    public static AggregateExpression Avg(string column) =>
        new(AggregateFunction.Average, ColumnReference.Parse(column));

    public static AggregateExpression Avg(ColumnReference column) =>
        new(AggregateFunction.Average, column);

    public static AggregateExpression Min(string column) =>
        new(AggregateFunction.Minimum, ColumnReference.Parse(column));

    public static AggregateExpression Min(ColumnReference column) =>
        new(AggregateFunction.Minimum, column);

    public static AggregateExpression Max(string column) =>
        new(AggregateFunction.Maximum, ColumnReference.Parse(column));

    public static AggregateExpression Max(ColumnReference column) =>
        new(AggregateFunction.Maximum, column);

    public static SelectQueryBuilder Select(params string[] columns) =>
        new((columns.Length == 0 ? ["*"] : columns).Select(ColumnReference.Parse));

    public static SelectQueryBuilder Select(
        ColumnReference column,
        params ColumnReference[] columns) =>
        new([column, .. columns]);

    public static SelectQueryBuilder From(string table) =>
        new SelectQueryBuilder([new ColumnReference("*")]).From(table);

    public static SelectQueryBuilder From(TableReference table) =>
        new SelectQueryBuilder([new ColumnReference("*")]).From(table);

    public static TableReference Table(string name, string? schema = null, string? alias = null)
    {
        if (schema is not null)
        {
            return new TableReference(name, schema, alias);
        }

        var table = TableReference.Parse(name);
        if (alias is not null && table.Alias is not null)
        {
            throw new ArgumentException("The table alias was specified both inline and separately.");
        }

        return alias is null ? table : table.As(alias);
    }

    public static ColumnReference Col(string name) => ColumnReference.Parse(name);

    public static InsertQueryBuilder InsertInto(string table) => new(TableReference.Parse(table));

    public static InsertQueryBuilder InsertInto(TableReference table) => new(table);

    public static UpdateQueryBuilder Update(string table) => new(TableReference.Parse(table));

    public static UpdateQueryBuilder Update(TableReference table) => new(table);

    public static DeleteQueryBuilder DeleteFrom(string table) => new(TableReference.Parse(table));

    public static DeleteQueryBuilder DeleteFrom(TableReference table) => new(table);
}

public interface ISqlQueryBuilder
{
    SqlQuery Build(IQueryCompiler? compiler = null);
}

public sealed class SelectQueryBuilder : ISqlQueryBuilder
{
    private readonly List<ISelectExpression> _columns;
    private readonly List<PredicateExpression> _predicates = [];
    private readonly List<PredicateExpression> _having = [];
    private readonly List<JoinClause> _joins = [];
    private readonly List<ColumnReference> _groups = [];
    private readonly List<(ColumnReference Column, bool Descending)> _sorts = [];
    private TableReference? _table;
    private int? _limit;
    private int? _offset;
    private bool _distinct;

    internal SelectQueryBuilder(IEnumerable<ISelectExpression> columns)
    {
        _columns = [.. columns];
    }

    public SelectQueryBuilder From(string table)
        => From(TableReference.Parse(table));

    public SelectQueryBuilder From(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        _table = table;
        return this;
    }

    public SelectQueryBuilder Select(params string[] columns)
    {
        _columns.Clear();
        _columns.AddRange((columns.Length == 0 ? ["*"] : columns)
            .Select(ColumnReference.Parse));
        return this;
    }

    public SelectQueryBuilder Select(
        ColumnReference column,
        params ColumnReference[] columns)
    {
        ArgumentNullException.ThrowIfNull(column);
        _columns.Clear();
        _columns.Add(column);
        _columns.AddRange(columns);
        return this;
    }

    public SelectQueryBuilder Select(
        ISelectExpression expression,
        params ISelectExpression[] expressions)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _columns.Clear();
        _columns.Add(expression);
        _columns.AddRange(expressions);
        return this;
    }

    public SelectQueryBuilder Distinct(bool distinct = true)
    {
        _distinct = distinct;
        return this;
    }

    public SelectQueryBuilder GroupBy(params string[] columns)
    {
        _groups.AddRange(columns.Select(ColumnReference.Parse));
        return this;
    }

    public SelectQueryBuilder GroupBy(
        ColumnReference column,
        params ColumnReference[] columns)
    {
        _groups.Add(column);
        _groups.AddRange(columns);
        return this;
    }

    public SelectQueryBuilder Having(PredicateExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _having.Add(predicate);
        return this;
    }

    public SelectQueryBuilder Having(string criteria, object? parameters = null) =>
        Having(SqlParser.ParseCriteria(criteria, parameters));

    public SelectQueryBuilder Where(PredicateExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicates.Add(predicate);
        return this;
    }

    public SelectQueryBuilder Join(string table, PredicateExpression on) =>
        Join(TableReference.Parse(table), on);

    public SelectQueryBuilder Join(string clause, object? parameters = null)
    {
        var join = SqlParser.ParseJoin(clause, parameters);
        return Join(join.Table, join.Predicate);
    }

    public SelectQueryBuilder Join(TableReference table, PredicateExpression on) =>
        AddJoin(JoinType.Inner, table, on);

    public SelectQueryBuilder InnerJoin(string table, PredicateExpression on) =>
        Join(table, on);

    public SelectQueryBuilder InnerJoin(string clause, object? parameters = null) =>
        Join(clause, parameters);

    public SelectQueryBuilder InnerJoin(TableReference table, PredicateExpression on) =>
        Join(table, on);

    public SelectQueryBuilder LeftJoin(string table, PredicateExpression on) =>
        LeftJoin(TableReference.Parse(table), on);

    public SelectQueryBuilder LeftJoin(string clause, object? parameters = null)
    {
        var join = SqlParser.ParseJoin(clause, parameters);
        return LeftJoin(join.Table, join.Predicate);
    }

    public SelectQueryBuilder LeftJoin(TableReference table, PredicateExpression on) =>
        AddJoin(JoinType.Left, table, on);

    public SelectQueryBuilder RightJoin(string table, PredicateExpression on) =>
        RightJoin(TableReference.Parse(table), on);

    public SelectQueryBuilder RightJoin(string clause, object? parameters = null)
    {
        var join = SqlParser.ParseJoin(clause, parameters);
        return RightJoin(join.Table, join.Predicate);
    }

    public SelectQueryBuilder RightJoin(TableReference table, PredicateExpression on) =>
        AddJoin(JoinType.Right, table, on);

    public SelectQueryBuilder FullJoin(string table, PredicateExpression on) =>
        FullJoin(TableReference.Parse(table), on);

    public SelectQueryBuilder FullJoin(string clause, object? parameters = null)
    {
        var join = SqlParser.ParseJoin(clause, parameters);
        return FullJoin(join.Table, join.Predicate);
    }

    public SelectQueryBuilder FullJoin(TableReference table, PredicateExpression on) =>
        AddJoin(JoinType.Full, table, on);

    public SelectQueryBuilder CrossJoin(string table) =>
        CrossJoin(TableReference.Parse(table));

    public SelectQueryBuilder CrossJoin(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        _joins.Add(new JoinClause(JoinType.Cross, table));
        return this;
    }

    public SelectQueryBuilder Where(string criteria, object? parameters = null) =>
        Where(SqlParser.ParseCriteria(criteria, parameters));

    public SelectQueryBuilder OrWhere(PredicateExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (_predicates.Count == 0)
        {
            _predicates.Add(predicate);
            return this;
        }

        var existing = _predicates.Aggregate((left, right) => left & right);
        _predicates.Clear();
        _predicates.Add(existing | predicate);
        return this;
    }

    public SelectQueryBuilder OrWhere(string criteria, object? parameters = null) =>
        OrWhere(SqlParser.ParseCriteria(criteria, parameters));

    public SelectQueryBuilder OrderBy(string column)
        => OrderBy(ColumnReference.Parse(column));

    public SelectQueryBuilder OrderBy(ColumnReference column)
    {
        _sorts.Add((column, false));
        return this;
    }

    public SelectQueryBuilder OrderByDescending(string column)
        => OrderByDescending(ColumnReference.Parse(column));

    public SelectQueryBuilder OrderByDescending(ColumnReference column)
    {
        _sorts.Add((column, true));
        return this;
    }

    public SelectQueryBuilder Limit(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        _limit = limit;
        return this;
    }

    public SelectQueryBuilder Offset(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        _offset = offset;
        return this;
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) =>
        (compiler ?? SqlDialect.Ansi).Compile(this);

    internal IReadOnlyList<ISelectExpression> Columns => _columns;
    internal bool IsDistinct => _distinct;
    internal IReadOnlyList<ColumnReference> Groups => _groups;
    internal IReadOnlyList<PredicateExpression> HavingPredicates => _having;
    internal IReadOnlyList<PredicateExpression> Predicates => _predicates;
    internal IReadOnlyList<JoinClause> Joins => _joins;
    internal IReadOnlyList<(ColumnReference Column, bool Descending)> Sorts => _sorts;
    internal TableReference Table => _table ?? throw new InvalidOperationException("A SELECT query requires From().");
    internal int? LimitValue => _limit;
    internal int? OffsetValue => _offset;

    internal SelectQueryBuilder Clone(int? maximumRows = null)
    {
        var clone = new SelectQueryBuilder(_columns)
        {
            _table = _table,
            _distinct = _distinct,
            _limit = maximumRows is null
                ? _limit
                : Math.Min(_limit ?? maximumRows.Value, maximumRows.Value),
            _offset = _offset
        };
        clone._predicates.AddRange(_predicates);
        clone._having.AddRange(_having);
        clone._joins.AddRange(_joins);
        clone._groups.AddRange(_groups);
        clone._sorts.AddRange(_sorts);
        return clone;
    }

    internal SelectQueryBuilder CloneWithoutPaging()
    {
        var clone = Clone();
        clone._limit = null;
        clone._offset = null;
        clone._sorts.Clear();
        return clone;
    }

    private SelectQueryBuilder AddJoin(
        JoinType type,
        TableReference table,
        PredicateExpression on)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(on);
        _joins.Add(new JoinClause(type, table, on));
        return this;
    }
}

internal sealed class CountQueryBuilder(SelectQueryBuilder query) : ISqlQueryBuilder
{
    internal SelectQueryBuilder Query { get; } = query;

    public SqlQuery Build(IQueryCompiler? compiler = null)
    {
        var inner = Query.Build(compiler);
        return new SqlQuery(
            $"SELECT COUNT(*) FROM ({inner.Text}) AS snap_count",
            inner.Parameters);
    }
}

public sealed record JoinClause
{
    internal JoinClause(JoinType type, TableReference table, PredicateExpression? predicate = null)
    {
        Type = type;
        Table = table;
        Predicate = predicate;
    }

    public JoinType Type { get; }

    public TableReference Table { get; }

    public PredicateExpression? Predicate { get; }
}

public enum JoinType
{
    Inner,
    Left,
    Right,
    Full,
    Cross
}
