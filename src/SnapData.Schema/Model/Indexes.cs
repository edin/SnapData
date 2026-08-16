namespace SnapData.Schema;

public sealed record IndexSchema
{
    public IndexSchema(
        string name,
        IEnumerable<IndexColumnSchema> columns,
        bool isUnique = false,
        string? filterExpression = null,
        SchemaIndexOrigin origin = SchemaIndexOrigin.Created,
        string? definitionSql = null,
        bool isVisible = true,
        string? method = null)
    {
        Name = SchemaModelGuard.RequiredName(name, nameof(name));
        ArgumentNullException.ThrowIfNull(columns);
        var snapshot = columns.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("At least one index column is required.", nameof(columns));
        }

        if (snapshot.Select(column => column.Ordinal).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Index column ordinals must be unique.", nameof(columns));
        }

        Columns = Array.AsReadOnly(snapshot);
        IsUnique = isUnique;
        FilterExpression = filterExpression;
        Origin = origin;
        DefinitionSql = definitionSql;
        IsVisible = isVisible;
        Method = method;
    }

    public string Name { get; }

    public IReadOnlyList<IndexColumnSchema> Columns { get; }

    public bool IsUnique { get; }

    public string? FilterExpression { get; }

    public SchemaIndexOrigin Origin { get; }

    public string? DefinitionSql { get; }

    public bool IsVisible { get; }

    public string? Method { get; }
}

public sealed record IndexColumnSchema
{
    public IndexColumnSchema(
        string? name,
        int ordinal,
        bool descending = false,
        string? expression = null,
        bool isIncluded = false,
        int? prefixLength = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        if (string.IsNullOrWhiteSpace(name) == string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException(
                "An index entry must define either a column name or an expression, but not both.");
        }

        if (prefixLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        Name = name;
        Ordinal = ordinal;
        Descending = descending;
        Expression = expression;
        IsIncluded = isIncluded;
        PrefixLength = prefixLength;
    }

    public string? Name { get; }

    public int Ordinal { get; }

    public bool Descending { get; }

    public string? Expression { get; }

    public bool IsIncluded { get; }

    public int? PrefixLength { get; }
}

public enum SchemaIndexOrigin
{
    Created,
    UniqueConstraint,
    PrimaryKey
}
