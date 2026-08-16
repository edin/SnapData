namespace SnapData.Schema;

public sealed class DatabaseSchema
{
    public DatabaseSchema(
        string? name,
        IEnumerable<TableSchema>? tables = null,
        IEnumerable<ViewSchema>? views = null)
    {
        Name = name;
        Tables = SchemaModelGuard.Snapshot(tables);
        Views = SchemaModelGuard.Snapshot(views);
    }

    public string? Name { get; }

    public IReadOnlyList<TableSchema> Tables { get; }

    public IReadOnlyList<ViewSchema> Views { get; }

}
