namespace SnapData.Schema;

public sealed class SchemaReadOptions
{
    public static SchemaReadOptions Default { get; } = new();

    public bool IncludeColumns { get; init; } = true;

    public bool IncludePrimaryKeys { get; init; } = true;

    public bool IncludeForeignKeys { get; init; } = true;

    public bool IncludeIndexes { get; init; } = true;

    public bool IncludeViews { get; init; } = true;

    public bool IncludeDefinitionSql { get; init; } = true;
}
