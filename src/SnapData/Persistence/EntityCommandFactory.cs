namespace SnapData;

public sealed class EntityCommandFactory : IEntityCommandFactory
{
    private readonly IEntityMappingProvider _mappingProvider;

    public EntityCommandFactory(IEntityMappingProvider? mappingProvider = null)
    {
        _mappingProvider = mappingProvider ?? EntityMappingProvider.Default;
    }

    public InsertQueryBuilder Insert<T>(T entity)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        var mapping = GetMapping(entity);
        if (mapping.InsertableProperties.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity {mapping.EntityType.Name} has no insertable properties.");
        }

        var builder = Sql.InsertInto(mapping.Table);
        foreach (var property in mapping.InsertableProperties)
        {
            builder.Value(property.Column, property.GetValue(entity));
        }

        return builder;
    }

    public UpdateQueryBuilder Update<T>(T entity)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        var mapping = GetMapping(entity);
        EnsureHasKeys(mapping, "update");
        if (mapping.UpdatableProperties.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity {mapping.EntityType.Name} has no updatable properties.");
        }

        var builder = Sql.Update(mapping.Table);
        foreach (var property in mapping.UpdatableProperties)
        {
            builder.Set(property.Column, property.GetValue(entity));
        }

        ApplyKeys(builder, entity, mapping);
        return builder;
    }

    public DeleteQueryBuilder Delete<T>(T entity)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        var mapping = GetMapping(entity);
        EnsureHasKeys(mapping, "delete");
        var builder = Sql.DeleteFrom(mapping.Table);
        ApplyKeys(builder, entity, mapping);
        return builder;
    }

    private EntityMapping GetMapping(object entity) =>
        _mappingProvider.GetMapping(entity.GetType());

    private static void ApplyKeys<TBuilder>(
        ConditionalMutationBuilder<TBuilder> builder,
        object entity,
        EntityMapping mapping)
        where TBuilder : ConditionalMutationBuilder<TBuilder>
    {
        foreach (var key in mapping.Keys)
        {
            var value = key.GetValue(entity);
            if (value is null)
            {
                throw new InvalidOperationException(
                    $"Key {mapping.EntityType.Name}.{key.PropertyName} cannot be null.");
            }

            if (key.Generated == GeneratedKind.Identity && IsDefaultValue(value, key.ValueType))
            {
                throw new InvalidOperationException(
                    $"Identity key {mapping.EntityType.Name}.{key.PropertyName} has not been generated.");
            }

            builder.Where(Exp.Col(key.Column) == value);
        }
    }

    private static void EnsureHasKeys(EntityMapping mapping, string operation)
    {
        if (mapping.Keys.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity {mapping.EntityType.Name} requires at least one key to {operation}.");
        }
    }

    private static bool IsDefaultValue(object value, Type valueType) =>
        value.Equals(valueType.IsValueType ? Activator.CreateInstance(valueType) : null);
}
