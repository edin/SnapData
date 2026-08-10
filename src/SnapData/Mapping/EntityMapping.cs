namespace SnapData;

public sealed class EntityMapping
{
    private readonly IReadOnlyDictionary<string, PropertyMapping> _propertiesByName;
    private readonly IReadOnlyDictionary<string, PropertyMapping> _propertiesByColumn;

    internal EntityMapping(
        Type entityType,
        string tableName,
        string? schema,
        IReadOnlyList<PropertyMapping> properties,
        IReadOnlyList<RelationMapping>? relations = null)
    {
        EntityType = entityType;
        TableName = tableName;
        Schema = schema;
        Table = new TableReference(tableName, schema);
        Properties = properties;
        Keys = properties.Where(property => property.IsKey).ToArray();
        SelectableProperties = properties.Where(property => property.IsSelectable).ToArray();
        InsertableProperties = properties.Where(property => property.IsInsertable).ToArray();
        UpdatableProperties = properties.Where(property => property.IsUpdatable).ToArray();
        Relations = relations ?? [];
        _propertiesByName = properties.ToDictionary(
            property => property.PropertyName,
            StringComparer.OrdinalIgnoreCase);
        _propertiesByColumn = properties.ToDictionary(
            property => property.ColumnName,
            StringComparer.OrdinalIgnoreCase);
    }

    public Type EntityType { get; }

    public string TableName { get; }

    public string? Schema { get; }

    public TableReference Table { get; }

    public string QualifiedTableName =>
        string.IsNullOrWhiteSpace(Schema) ? TableName : $"{Schema}.{TableName}";

    public IReadOnlyList<PropertyMapping> Properties { get; }

    public IReadOnlyList<PropertyMapping> Keys { get; }

    public IReadOnlyList<PropertyMapping> SelectableProperties { get; }

    public IReadOnlyList<PropertyMapping> InsertableProperties { get; }

    public IReadOnlyList<PropertyMapping> UpdatableProperties { get; }

    public IReadOnlyList<RelationMapping> Relations { get; }

    public PropertyMapping? SingleKey => Keys.Count == 1 ? Keys[0] : null;

    public PropertyMapping? FindProperty(string name) =>
        _propertiesByName.GetValueOrDefault(name);

    public PropertyMapping? FindColumn(string name) =>
        _propertiesByColumn.GetValueOrDefault(name);

    public RelationMapping? FindRelation(string navigationName) =>
        Relations.FirstOrDefault(relation => string.Equals(
            relation.NavigationName,
            navigationName,
            StringComparison.OrdinalIgnoreCase));
}
