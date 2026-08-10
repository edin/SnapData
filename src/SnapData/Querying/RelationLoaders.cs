namespace SnapData;

internal interface IEntityRelationLoader<T> where T : class
{
    RelationMapping Relation { get; }

    Task LoadAsync(
        IReadOnlyList<T> entities,
        IDbExecutor executor,
        QueryOptions? options,
        CancellationToken cancellationToken);
}

internal sealed class ReferenceRelationLoader<T, TRelated>(
    RelationMapping relation,
    IEntityMappingProvider mappingProvider) : IEntityRelationLoader<T>
    where T : class
    where TRelated : class
{
    private const int BatchSize = 500;

    public RelationMapping Relation { get; } = relation;

    public async Task LoadAsync(
        IReadOnlyList<T> entities,
        IDbExecutor executor,
        QueryOptions? options,
        CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
        {
            return;
        }

        var relatedMapping = mappingProvider.GetMapping<TRelated>();
        var foreignKey = Relation.ResolveForeignKey(mappingProvider);
        var keys = entities
            .Select(entity => Relation.LocalKey.GetValue(entity))
            .Where(value => value is not null)
            .Distinct()
            .ToArray();
        var relatedByKey = new Dictionary<object, TRelated>();
        foreach (var batch in keys.Chunk(BatchSize))
        {
            var query = Sql
                .Select(
                    relatedMapping.SelectableProperties[0].Column,
                    relatedMapping.SelectableProperties.Skip(1)
                        .Select(property => property.Column)
                        .ToArray())
                .From(relatedMapping.Table)
                .Where(Exp.Col(foreignKey.Column).In(batch));
            var relatedItems = await executor.QueryAsync<TRelated>(
                query,
                options,
                cancellationToken);
            foreach (var related in relatedItems)
            {
                var key = foreignKey.GetValue(related)
                    ?? throw new InvalidOperationException(
                        $"Relation foreign key {relatedMapping.EntityType.Name}.{foreignKey.PropertyName} cannot be null in loaded results.");
                if (!relatedByKey.TryAdd(key, related))
                {
                    throw new InvalidOperationException(
                        $"Reference relation {typeof(T).Name}.{Relation.NavigationName} returned more than one {typeof(TRelated).Name} for key '{key}'.");
                }
            }
        }

        foreach (var entity in entities)
        {
            var key = Relation.LocalKey.GetValue(entity);
            Relation.Navigation.SetValue(
                entity,
                key is not null && relatedByKey.TryGetValue(key, out var related)
                    ? related
                    : null);
        }
    }
}
