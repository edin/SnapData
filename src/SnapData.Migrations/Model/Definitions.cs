using SnapData.Schema;

namespace SnapData.Migrations;

public sealed record ColumnDefinition(
    string Name,
    MigrationColumnType Type,
    bool IsNullable = false,
    bool IsPrimaryKey = false,
    bool IsUnique = false,
    bool IsIdentity = false,
    object? DefaultValue = null,
    int? Length = null,
    int? Precision = null,
    int? Scale = null);

public sealed record IndexColumn(
    string Name,
    MigrationSortOrder Order = MigrationSortOrder.Ascending)
{
    public static IndexColumn Asc(string name) => new(name);

    public static IndexColumn Desc(string name) =>
        new(name, MigrationSortOrder.Descending);

    public static implicit operator IndexColumn(string name) => new(name);
}

public sealed record IndexDefinition
{
    public IndexDefinition(
        string? name,
        IEnumerable<IndexColumn> columns,
        bool isUnique = false)
    {
        var snapshot = columns?.ToArray() ?? throw new ArgumentNullException(nameof(columns));
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("An index needs at least one column.", nameof(columns));
        }

        Name = name;
        Columns = Array.AsReadOnly(snapshot);
        IsUnique = isUnique;
    }

    public string? Name { get; }

    public IReadOnlyList<IndexColumn> Columns { get; }

    public bool IsUnique { get; }
}

public sealed record ForeignKeyDefinition
{
    public ForeignKeyDefinition(
        string? name,
        IEnumerable<string> columns,
        string referencedTable,
        IEnumerable<string> referencedColumns,
        ReferentialAction onUpdate = ReferentialAction.NoAction,
        ReferentialAction onDelete = ReferentialAction.NoAction)
    {
        var local = columns?.ToArray() ?? throw new ArgumentNullException(nameof(columns));
        var referenced = referencedColumns?.ToArray()
            ?? throw new ArgumentNullException(nameof(referencedColumns));
        if (local.Length == 0 || local.Length != referenced.Length)
        {
            throw new ArgumentException(
                "Foreign-key local and referenced columns must have matching non-empty counts.",
                nameof(referencedColumns));
        }

        Name = name;
        Columns = Array.AsReadOnly(local);
        ReferencedTable = RequiredName(referencedTable, nameof(referencedTable));
        ReferencedColumns = Array.AsReadOnly(referenced);
        OnUpdate = onUpdate;
        OnDelete = onDelete;
    }

    public string? Name { get; }
    public IReadOnlyList<string> Columns { get; }
    public string ReferencedTable { get; }
    public IReadOnlyList<string> ReferencedColumns { get; }
    public ReferentialAction OnUpdate { get; }
    public ReferentialAction OnDelete { get; }

    private static string RequiredName(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A name is required.", parameter)
            : value;
}
