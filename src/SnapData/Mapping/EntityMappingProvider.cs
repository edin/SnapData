using System.Collections.Concurrent;
using System.Reflection;

namespace SnapData;

public sealed class EntityMappingProvider : IEntityMappingProvider
{
    private readonly ConcurrentDictionary<Type, EntityMapping> _cache = new();
    private readonly MappingOptions _options;

    public EntityMappingProvider(MappingOptions? options = null)
    {
        _options = options ?? new MappingOptions();
    }

    public static EntityMappingProvider Default { get; } = new();

    public EntityMapping GetMapping(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return _cache.GetOrAdd(entityType, BuildMapping);
    }

    public EntityMapping GetMapping<T>() => GetMapping(typeof(T));

    private EntityMapping BuildMapping(Type entityType)
    {
        ValidateEntityType(entityType);
        var table = entityType.GetCustomAttribute<TableAttribute>();
        var (tableName, schema) = ResolveTable(entityType, table);
        ValidateName(tableName, $"table for entity {entityType.Name}");

        var candidates = entityType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToArray();

        var relationProperties = candidates
            .Where(property => property.IsDefined(typeof(RelationAttribute), inherit: true))
            .ToArray();

        foreach (var property in candidates.Where(property =>
                     property.IsDefined(typeof(IgnoreAttribute), inherit: true)
                     && property.IsDefined(typeof(KeyAttribute), inherit: true)))
        {
            throw MappingError(entityType, property,
                "cannot be marked with both Key and Ignore.");
        }

        var mapped = candidates
            .Where(property => !property.IsDefined(typeof(IgnoreAttribute), inherit: true)
                && !property.IsDefined(typeof(RelationAttribute), inherit: true))
            .ToArray();
        var hasExplicitKey = mapped.Any(property =>
            property.IsDefined(typeof(KeyAttribute), inherit: true));
        var mappings = mapped.Select(property =>
            CreatePropertyMapping(entityType, property, hasExplicitKey)).ToArray();

        if (mappings.Length == 0)
        {
            throw new InvalidOperationException(
                $"Entity {entityType.Name} does not contain any mapped properties.");
        }

        if (schema is not null)
        {
            ValidateName(schema, $"schema for entity {entityType.Name}");
        }

        ValidateMappings(entityType, mappings);
        var relations = relationProperties
            .Select(property => CreateRelationMapping(entityType, property, mappings))
            .ToArray();
        return new EntityMapping(entityType, tableName, schema, mappings, relations);
    }

    private static RelationMapping CreateRelationMapping(
        Type entityType,
        PropertyInfo navigation,
        IReadOnlyList<PropertyMapping> properties)
    {
        if (navigation.IsDefined(typeof(IgnoreAttribute), inherit: true))
        {
            throw MappingError(entityType, navigation,
                "cannot be marked with both Relation and Ignore.");
        }

        var attribute = navigation.GetCustomAttribute<RelationAttribute>(inherit: true)!;
        var localKey = properties.FirstOrDefault(property => string.Equals(
            property.PropertyName,
            attribute.LocalKey,
            StringComparison.OrdinalIgnoreCase))
            ?? throw MappingError(entityType, navigation,
                $"references unmapped local property '{attribute.LocalKey}'.");
        var (relatedType, cardinality) = GetRelatedType(entityType, navigation);
        if (relatedType == typeof(string) || relatedType.IsValueType)
        {
            throw MappingError(entityType, navigation,
                "must reference an entity class or List<T>.");
        }

        if (navigation.SetMethod is null && cardinality == RelationCardinality.Reference)
        {
            throw MappingError(entityType, navigation,
                "reference navigation must be writable.");
        }

        return new RelationMapping(
            navigation,
            localKey,
            relatedType,
            attribute.ForeignKey,
            cardinality);
    }

    private static (Type RelatedType, RelationCardinality Cardinality) GetRelatedType(
        Type entityType,
        PropertyInfo navigation)
    {
        var type = navigation.PropertyType;
        if (type.IsGenericType && IsSupportedCollection(type.GetGenericTypeDefinition()))
        {
            return (type.GetGenericArguments()[0], RelationCardinality.Collection);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            throw MappingError(entityType, navigation,
                "cannot use a nullable value type as a navigation.");
        }

        return (type, RelationCardinality.Reference);
    }

    private static bool IsSupportedCollection(Type genericType) =>
        genericType == typeof(List<>)
        || genericType == typeof(IList<>)
        || genericType == typeof(ICollection<>)
        || genericType == typeof(IReadOnlyList<>);

    private (string TableName, string? Schema) ResolveTable(
        Type entityType,
        TableAttribute? table)
    {
        if (table is null)
        {
            return (_options.TableName(entityType), null);
        }

        var parts = table.Name.Split('.');
        if (parts.Length == 1)
        {
            return (table.Name, table.Schema);
        }

        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                $"Entity {entityType.Name} table name '{table.Name}' must use the form 'schema.table'.");
        }

        if (table.Schema is not null)
        {
            throw new InvalidOperationException(
                $"Entity {entityType.Name} specifies schema both inline and through TableAttribute.Schema.");
        }

        return (parts[1], parts[0]);
    }

    private PropertyMapping CreatePropertyMapping(
        Type entityType,
        PropertyInfo property,
        bool hasExplicitKey)
    {
        var columnName = property.GetCustomAttribute<ColumnAttribute>()?.Name
            ?? _options.ColumnName(property);
        ValidateName(columnName, $"column for {entityType.Name}.{property.Name}");
        var isKey = hasExplicitKey
            ? property.IsDefined(typeof(KeyAttribute), inherit: true)
            : _options.KeyConvention(entityType, property);
        var generated = property.GetCustomAttribute<GeneratedAttribute>()?.Kind
            ?? GeneratedKind.Never;

        return new PropertyMapping(
            property,
            columnName,
            IsNullable(property),
            isKey,
            generated);
    }

    private bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        return new NullabilityInfoContext().Create(property).ReadState
            != NullabilityState.NotNull;
    }

    private static void ValidateEntityType(Type entityType)
    {
        if (!entityType.IsClass || entityType.IsAbstract || entityType.ContainsGenericParameters)
        {
            throw new InvalidOperationException(
                $"Entity type {entityType} must be a concrete, closed class.");
        }
    }

    private static void ValidateMappings(
        Type entityType,
        IReadOnlyList<PropertyMapping> mappings)
    {
        var duplicate = mappings
            .GroupBy(property => property.ColumnName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Entity {entityType.Name} maps multiple properties to column '{duplicate.Key}'.");
        }

        var identities = mappings.Where(property =>
            property.Generated == GeneratedKind.Identity).ToArray();
        if (identities.Length > 1)
        {
            throw new InvalidOperationException(
                $"Entity {entityType.Name} defines multiple identity-generated properties.");
        }

        if (identities.SingleOrDefault() is { IsKey: false } identity)
        {
            throw MappingError(entityType, identity.Property,
                "is identity-generated but is not a key.");
        }
    }

    private static void ValidateName(string? name, string target)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"The mapped {target} has an empty name.");
        }
    }

    private static InvalidOperationException MappingError(
        Type entity,
        PropertyInfo property,
        string message) =>
        new($"Entity mapping {entity.Name}.{property.Name} {message}");
}
