using SnapData.Schema;

namespace SnapData.Migrations;

public sealed record ColumnDefinition
{
    public ColumnDefinition(
        string Name,
        MigrationColumnType Type,
        bool IsNullable = false,
        bool IsPrimaryKey = false,
        bool IsUnique = false,
        bool IsIdentity = false,
        object? DefaultValue = null,
        int? Length = null,
        int? Precision = null,
        int? Scale = null)
    {
        this.Name = RequiredName(Name, nameof(Name));
        if (!Enum.IsDefined(Type))
        {
            throw new ArgumentOutOfRangeException(nameof(Type));
        }
        if (Length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Length));
        }
        if (Precision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Precision));
        }
        if (Scale < 0 || Scale > Precision)
        {
            throw new ArgumentOutOfRangeException(nameof(Scale));
        }
        if (Scale is not null && Precision is null)
        {
            throw new ArgumentException("Scale requires precision.", nameof(Scale));
        }

        this.Type = Type;
        this.IsNullable = IsNullable;
        this.IsPrimaryKey = IsPrimaryKey;
        this.IsUnique = IsUnique;
        this.IsIdentity = IsIdentity;
        this.DefaultValue = DefaultValue;
        this.Length = Length;
        this.Precision = Precision;
        this.Scale = Scale;
    }

    public string Name { get; init; }
    public MigrationColumnType Type { get; init; }
    public bool IsNullable { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsUnique { get; init; }
    public bool IsIdentity { get; init; }
    public object? DefaultValue { get; init; }
    public int? Length { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }

    private static string RequiredName(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A name is required.", parameter)
            : value;
}

public sealed record IndexColumn
{
    public IndexColumn(
        string Name,
        MigrationSortOrder Order = MigrationSortOrder.Ascending)
    {
        this.Name = string.IsNullOrWhiteSpace(Name)
            ? throw new ArgumentException("A name is required.", nameof(Name))
            : Name;
        this.Order = Enum.IsDefined(Order)
            ? Order
            : throw new ArgumentOutOfRangeException(nameof(Order));
    }

    public string Name { get; init; }
    public MigrationSortOrder Order { get; init; }

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
        if (name is not null && string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An index name cannot be empty.", nameof(name));
        }
        var duplicate = snapshot
            .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Index column '{duplicate.Key}' is repeated.", nameof(columns));
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
        if (local.Any(string.IsNullOrWhiteSpace) || referenced.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Foreign-key column names cannot be empty.", nameof(columns));
        }
        if (name is not null && string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A foreign-key name cannot be empty.", nameof(name));
        }
        if (!Enum.IsDefined(onUpdate))
        {
            throw new ArgumentOutOfRangeException(nameof(onUpdate));
        }
        if (!Enum.IsDefined(onDelete))
        {
            throw new ArgumentOutOfRangeException(nameof(onDelete));
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

public sealed record CheckConstraintDefinition
{
    public CheckConstraintDefinition(string name, string predicate)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A check-constraint name is required.", nameof(name))
            : name;
        Predicate = string.IsNullOrWhiteSpace(predicate)
            ? throw new ArgumentException("A check-constraint predicate is required.", nameof(predicate))
            : predicate;
    }

    public string Name { get; }

    public string Predicate { get; }
}
