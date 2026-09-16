namespace SnapData;

/// <summary>A single-row insert with an explicit unique conflict target and action.</summary>
public sealed class UpsertQueryBuilder : ISqlQueryBuilder
{
    internal InsertQueryBuilder Insert { get; }
    internal IReadOnlyList<ColumnReference> ConflictColumns { get; private set; } = [];
    internal IReadOnlyList<ColumnReference> UpdatedColumns { get; private set; } = [];
    internal bool IgnoreConflict { get; private set; }
    internal IReadOnlyList<ColumnReference> ReturningColumns { get; private set; } = [];
    private bool actionSelected;

    internal UpsertQueryBuilder(TableReference table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Alias is not null)
            throw new ArgumentException("Upsert targets cannot have aliases.", nameof(table));
        Insert = new InsertQueryBuilder(table);
    }

    public UpsertQueryBuilder Values(object values)
    {
        Insert.Values(values);
        return this;
    }

    public UpsertQueryBuilder Value(string column, object? value)
    {
        Insert.Value(ReadColumns([column])[0], value);
        return this;
    }

    /// <summary>Names columns backed by a primary key or unique constraint.</summary>
    public UpsertQueryBuilder OnConflict(params string[] columns)
    {
        ConflictColumns = ReadColumns(columns);
        return this;
    }

    /// <summary>Copies only these columns from the proposed insert into the existing row.</summary>
    public UpsertQueryBuilder UpdateColumns(params string[] columns)
    {
        UpdatedColumns = ReadColumns(columns);
        IgnoreConflict = false;
        actionSelected = true;
        return this;
    }

    public UpsertQueryBuilder DoNothing()
    {
        UpdatedColumns = [];
        IgnoreConflict = true;
        actionSelected = true;
        return this;
    }

    public SqlQuery Build(IQueryCompiler? compiler = null) =>
        (compiler ?? SqlDialect.Ansi).Compile(this);

    public UpsertQueryBuilder Returning(params string[] columns)
    {
        ReturningColumns = ReadColumns(columns);
        return this;
    }

    internal void Validate()
    {
        if (ConflictColumns.Count == 0 || !actionSelected)
            throw new InvalidOperationException("Upsert requires OnConflict() and UpdateColumns() or DoNothing().");
        var rows = Insert.RowsValue;
        if (rows.Count != 1)
            throw new InvalidOperationException("Upsert requires exactly one inserted row.");
        foreach (var column in rows[0].Select(pair => pair.Key))
            ValidateColumn(column);
        if (rows[0].Select(pair => pair.Key.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows[0].Count)
            throw new InvalidOperationException("Duplicate inserted columns are not allowed in an upsert.");
        foreach (var column in ConflictColumns.Concat(UpdatedColumns))
        {
            if (!rows[0].Any(pair => InsertQueryBuilder.SameColumn(pair.Key, column)))
                throw new InvalidOperationException($"Upsert column '{column.Name}' requires an inserted value.");
        }
        if (UpdatedColumns.Any(column => ConflictColumns.Any(key => InsertQueryBuilder.SameColumn(key, column))))
            throw new InvalidOperationException("Upsert cannot update conflict columns.");
    }

    private static ColumnReference[] ReadColumns(string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Length == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));
        var result = columns.Select(ColumnReference.Parse).ToArray();
        foreach (var column in result) ValidateColumn(column);
        if (result.Select(column => column.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
            throw new ArgumentException("Duplicate upsert columns are not allowed.", nameof(columns));
        return result;
    }

    private static void ValidateColumn(ColumnReference column)
    {
        if (column.Qualifier is not null || column.Alias is not null || column.Name == "*")
            throw new ArgumentException("Upsert columns must be unqualified names without aliases or wildcards.");
    }
}
