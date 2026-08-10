namespace SnapData;

public interface IEntityMappingProvider
{
    EntityMapping GetMapping(Type entityType);

    EntityMapping GetMapping<T>() => GetMapping(typeof(T));
}
