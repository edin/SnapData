namespace SnapData.Schema;

public sealed class TableSchema
{
    public TableSchema(
        SchemaObjectName name,
        IEnumerable<ColumnSchema>? columns = null,
        PrimaryKeySchema? primaryKey = null,
        IEnumerable<ForeignKeySchema>? foreignKeys = null,
        IEnumerable<IndexSchema>? indexes = null,
        string? definitionSql = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Columns = SchemaModelGuard.Snapshot(columns);
        PrimaryKey = primaryKey;
        ForeignKeys = SchemaModelGuard.Snapshot(foreignKeys);
        Indexes = SchemaModelGuard.Snapshot(indexes);
        DefinitionSql = definitionSql;
    }

    public SchemaObjectName Name { get; }

    public IReadOnlyList<ColumnSchema> Columns { get; }

    public PrimaryKeySchema? PrimaryKey { get; }

    public IReadOnlyList<ForeignKeySchema> ForeignKeys { get; }

    public IReadOnlyList<IndexSchema> Indexes { get; }

    public string? DefinitionSql { get; }

}

public sealed record ViewSchema
{
    public ViewSchema(
        SchemaObjectName name,
        IEnumerable<ColumnSchema>? columns = null,
        string? definitionSql = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Columns = SchemaModelGuard.Snapshot(columns);
        DefinitionSql = definitionSql;
    }

    public SchemaObjectName Name { get; }

    public IReadOnlyList<ColumnSchema> Columns { get; }

    public string? DefinitionSql { get; }
}
