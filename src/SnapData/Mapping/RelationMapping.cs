using System.Reflection;

namespace SnapData;

public sealed class RelationMapping
{
    internal RelationMapping(
        PropertyInfo navigation,
        PropertyMapping localKey,
        Type relatedType,
        string foreignKeyPropertyName,
        RelationCardinality cardinality)
    {
        Navigation = navigation;
        LocalKey = localKey;
        RelatedType = relatedType;
        ForeignKeyPropertyName = foreignKeyPropertyName;
        Cardinality = cardinality;
    }

    public PropertyInfo Navigation { get; }

    public string NavigationName => Navigation.Name;

    public PropertyMapping LocalKey { get; }

    public Type RelatedType { get; }

    public string ForeignKeyPropertyName { get; }

    public RelationCardinality Cardinality { get; }

    internal PropertyMapping ResolveForeignKey(IEntityMappingProvider mappingProvider)
    {
        var related = mappingProvider.GetMapping(RelatedType);
        var foreignKey = related.FindProperty(ForeignKeyPropertyName)
            ?? throw new InvalidOperationException(
                $"Relation {Navigation.DeclaringType?.Name}.{NavigationName} references unmapped foreign property {RelatedType.Name}.{ForeignKeyPropertyName}.");
        if (LocalKey.ValueType != foreignKey.ValueType)
        {
            throw new InvalidOperationException(
                $"Relation {Navigation.DeclaringType?.Name}.{NavigationName} key types do not match: {LocalKey.PropertyType.Name} and {foreignKey.PropertyType.Name}.");
        }

        return foreignKey;
    }
}

public enum RelationCardinality
{
    Reference,
    Collection
}
