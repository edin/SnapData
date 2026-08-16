namespace SnapData.Schema;

public sealed record PrimaryKeySchema
{
    public PrimaryKeySchema(string? name, IEnumerable<string> columns)
    {
        Name = name;
        Columns = SchemaModelGuard.RequiredNames(columns, nameof(columns));
    }

    public string? Name { get; }

    public IReadOnlyList<string> Columns { get; }
}

public sealed record ForeignKeySchema
{
    public ForeignKeySchema(
        string? name,
        IEnumerable<string> columns,
        SchemaObjectName referencedTable,
        IEnumerable<string> referencedColumns,
        ReferentialAction onUpdate = ReferentialAction.NoAction,
        ReferentialAction onDelete = ReferentialAction.NoAction)
    {
        ArgumentNullException.ThrowIfNull(referencedTable);
        Name = name;
        Columns = SchemaModelGuard.RequiredNames(columns, nameof(columns));
        ReferencedTable = referencedTable;
        ReferencedColumns = SchemaModelGuard.RequiredNames(
            referencedColumns,
            nameof(referencedColumns));
        if (Columns.Count != ReferencedColumns.Count)
        {
            throw new ArgumentException(
                "Foreign-key local and referenced column counts must match.",
                nameof(referencedColumns));
        }

        OnUpdate = onUpdate;
        OnDelete = onDelete;
    }

    public string? Name { get; }

    public IReadOnlyList<string> Columns { get; }

    public SchemaObjectName ReferencedTable { get; }

    public IReadOnlyList<string> ReferencedColumns { get; }

    public ReferentialAction OnUpdate { get; }

    public ReferentialAction OnDelete { get; }
}

public enum ReferentialAction
{
    NoAction,
    Restrict,
    Cascade,
    SetNull,
    SetDefault
}
