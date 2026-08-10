using System.Reflection;

namespace SnapData;

public sealed class MappingOptions
{
    public Func<Type, string> TableName { get; init; } = type => type.Name;

    public Func<PropertyInfo, string> ColumnName { get; init; } = property => property.Name;

    public Func<Type, PropertyInfo, bool> KeyConvention { get; init; } =
        static (entity, property) =>
            property.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
            || property.Name.Equals($"{entity.Name}Id", StringComparison.OrdinalIgnoreCase);
}
