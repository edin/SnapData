namespace SnapData;

public sealed class InsertQueryBuilder : ISqlQueryBuilder
{
    private readonly List<IReadOnlyList<KeyValuePair<ColumnReference, object?>>> _rows = [];
    private readonly List<KeyValuePair<ColumnReference, object?>> _values = [];
    private readonly List<ColumnReference> _returning = [];

    internal InsertQueryBuilder(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
    }

    internal TableReference Table { get; }
    internal IReadOnlyList<IReadOnlyList<KeyValuePair<ColumnReference, object?>>> RowsValue =>
        _rows.Count == 0 && _values.Count > 0 ? [_values] : _rows;
    internal IReadOnlyList<ColumnReference> ReturningColumns => _returning;

    public InsertQueryBuilder Values(object values)
    {
        ArgumentNullException.ThrowIfNull(values);
        EnsureRowsNotStarted();
        _values.Clear();
        _values.AddRange(ReadValues(values));
        return this;
    }

    public InsertQueryBuilder Value(string column, object? value)
        => Value(ColumnReference.Parse(column), value);

    public InsertQueryBuilder Value(ColumnReference column, object? value)
    {
        ArgumentNullException.ThrowIfNull(column);
        EnsureRowsNotStarted();
        if (_values.Any(pair => SameColumn(pair.Key, column)))
        {
            throw new InvalidOperationException($"Column '{column}' already has a value.");
        }

        _values.Add(new(column, value));
        return this;
    }

    public InsertQueryBuilder Rows(params object[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (_values.Count > 0)
        {
            throw new InvalidOperationException("Rows() cannot be combined with Values() or Value().");
        }

        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row);
            _rows.Add(ReadValues(row));
        }

        return this;
    }

    public InsertQueryBuilder Returning(params string[] columns)
    {
        AddReturning(_returning, columns.Select(ColumnReference.Parse));
        return this;
    }

    public InsertQueryBuilder Returning(
        ColumnReference column,
        params ColumnReference[] columns)
    {
        AddReturning(_returning, [column, .. columns]);
        return this;
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) =>
        (compiler ?? SqlDialect.Ansi).Compile(this);

    private void EnsureRowsNotStarted()
    {
        if (_rows.Count > 0)
        {
            throw new InvalidOperationException("Values() and Value() cannot be combined with Rows().");
        }
    }

    private static IReadOnlyList<KeyValuePair<ColumnReference, object?>> ReadValues(object values)
    {
        var result = ParameterReader.Read(values)
            .Select(pair => new KeyValuePair<ColumnReference, object?>(
                ColumnReference.Parse(pair.Key),
                pair.Value))
            .ToArray();
        if (result.Length == 0)
        {
            throw new ArgumentException("At least one column value is required.", nameof(values));
        }

        return result;
    }

    internal static void AddReturning(
        List<ColumnReference> target,
        IEnumerable<ColumnReference> columns)
    {
        foreach (var column in columns)
        {
            ArgumentNullException.ThrowIfNull(column);
            target.Add(column);
        }
    }

    internal static bool SameColumn(ColumnReference left, ColumnReference right) =>
        string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Qualifier, right.Qualifier, StringComparison.OrdinalIgnoreCase);
}

public abstract class ConditionalMutationBuilder<TBuilder> : ISqlQueryBuilder
    where TBuilder : ConditionalMutationBuilder<TBuilder>
{
    private PredicateExpression? _predicate;
    private bool _allRows;

    internal PredicateExpression? Predicate => _predicate;
    internal bool TargetsAllRows => _allRows;

    public TBuilder Where(PredicateExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        EnsureNotAllRows();
        _predicate = _predicate is null ? predicate : _predicate & predicate;
        return (TBuilder)this;
    }

    public TBuilder Where(string criteria, object? parameters = null) =>
        Where(SqlParser.ParseCriteria(criteria, parameters));

    public TBuilder OrWhere(PredicateExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        EnsureNotAllRows();
        _predicate = _predicate is null ? predicate : _predicate | predicate;
        return (TBuilder)this;
    }

    public TBuilder OrWhere(string criteria, object? parameters = null) =>
        OrWhere(SqlParser.ParseCriteria(criteria, parameters));

    public TBuilder AllRows()
    {
        if (_predicate is not null)
        {
            throw new InvalidOperationException("AllRows() cannot be combined with Where().");
        }

        _allRows = true;
        return (TBuilder)this;
    }

    public abstract SqlQuery Build(IQueryCompiler? compiler = null);

    internal void EnsureSafe()
    {
        if (_predicate is null && !_allRows)
        {
            throw new InvalidOperationException(
                "UPDATE and DELETE require Where() or an explicit AllRows().");
        }
    }

    private void EnsureNotAllRows()
    {
        if (_allRows)
        {
            throw new InvalidOperationException("Where() cannot be combined with AllRows().");
        }
    }
}

public sealed class UpdateQueryBuilder : ConditionalMutationBuilder<UpdateQueryBuilder>
{
    private readonly List<KeyValuePair<ColumnReference, object?>> _values = [];
    private readonly List<ColumnReference> _returning = [];

    internal UpdateQueryBuilder(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
    }

    internal TableReference Table { get; }
    internal IReadOnlyList<KeyValuePair<ColumnReference, object?>> Values => _values;
    internal IReadOnlyList<ColumnReference> ReturningColumns => _returning;

    public UpdateQueryBuilder Set(object values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var pair in ParameterReader.Read(values))
        {
            Set(pair.Key, pair.Value);
        }

        return this;
    }

    public UpdateQueryBuilder Set(string column, object? value)
        => Set(ColumnReference.Parse(column), value);

    public UpdateQueryBuilder Set(ColumnReference column, object? value)
    {
        ArgumentNullException.ThrowIfNull(column);
        var index = _values.FindIndex(pair =>
            InsertQueryBuilder.SameColumn(pair.Key, column));
        var pair = new KeyValuePair<ColumnReference, object?>(column, value);
        if (index < 0)
        {
            _values.Add(pair);
        }
        else
        {
            _values[index] = pair;
        }

        return this;
    }

    public UpdateQueryBuilder Returning(params string[] columns)
    {
        InsertQueryBuilder.AddReturning(_returning, columns.Select(ColumnReference.Parse));
        return this;
    }

    public UpdateQueryBuilder Returning(
        ColumnReference column,
        params ColumnReference[] columns)
    {
        InsertQueryBuilder.AddReturning(_returning, [column, .. columns]);
        return this;
    }

    public override SqlQuery Build(IQueryCompiler? compiler = null) =>
        (compiler ?? SqlDialect.Ansi).Compile(this);

}

public sealed class DeleteQueryBuilder : ConditionalMutationBuilder<DeleteQueryBuilder>
{
    private readonly List<ColumnReference> _returning = [];

    internal DeleteQueryBuilder(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
    }

    internal TableReference Table { get; }
    internal IReadOnlyList<ColumnReference> ReturningColumns => _returning;

    public DeleteQueryBuilder Returning(params string[] columns)
    {
        InsertQueryBuilder.AddReturning(_returning, columns.Select(ColumnReference.Parse));
        return this;
    }

    public DeleteQueryBuilder Returning(
        ColumnReference column,
        params ColumnReference[] columns)
    {
        InsertQueryBuilder.AddReturning(_returning, [column, .. columns]);
        return this;
    }

    public override SqlQuery Build(IQueryCompiler? compiler = null) =>
        (compiler ?? SqlDialect.Ansi).Compile(this);

}
