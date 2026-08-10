using System.Data.Common;
using System.Globalization;
using System.Reflection;

namespace SnapData;

internal interface IRowMapper<out T>
{
    T Map(DbDataReader reader);
}

internal static class RowMapper<T>
{
    internal static IRowMapper<T> Create(
        DbDataReader reader,
        IEntityMappingProvider mappingProvider)
    {
        var type = typeof(T);
        if (IsScalar(type))
        {
            return new ScalarMapper(type);
        }

        var mapping = mappingProvider.GetMapping(type);
        var columns = Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, StringComparer.OrdinalIgnoreCase);
        var constructor = SelectConstructor(type, mapping, columns);
        var constructorParameters = constructor?.GetParameters() ?? [];
        var constructorProperties = new HashSet<PropertyInfo>();
        var constructorBindings = constructorParameters.Select(parameter =>
        {
            var property = mapping.FindProperty(parameter.Name!);
            if (property is not null)
            {
                constructorProperties.Add(property.Property);
            }

            var columnName = property?.ColumnName ?? parameter.Name!;
            return columns.TryGetValue(columnName, out var ordinal)
                ? new ConstructorBinding(parameter, ordinal)
                : parameter.HasDefaultValue
                    ? new ConstructorBinding(parameter, null)
                    : throw new InvalidOperationException(
                        $"Column '{columnName}' is required to construct {type.Name}.");
        }).ToArray();
        var propertyBindings = mapping.Properties
            .Where(property => property.CanWrite
                && !constructorProperties.Contains(property.Property)
                && columns.ContainsKey(property.ColumnName))
            .Select(property => PropertyHydrationOperation<T>.Create(
                property,
                columns[property.ColumnName],
                reader.GetFieldType(columns[property.ColumnName])))
            .ToArray();

        if (constructor is not null && constructorParameters.Length > 0)
        {
            return new ConstructorMapper(
                constructor,
                constructorBindings,
                propertyBindings);
        }

        return new MutableMapper(
            static () => Activator.CreateInstance<T>(),
            propertyBindings);
    }

    private static ConstructorInfo? SelectConstructor(
        Type type,
        EntityMapping mapping,
        IReadOnlyDictionary<string, int> columns) =>
        type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.GetParameters().All(parameter =>
            {
                var column = mapping.FindProperty(parameter.Name!)?.ColumnName ?? parameter.Name!;
                return columns.ContainsKey(column) || parameter.HasDefaultValue;
            }))
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .FirstOrDefault();

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(Guid);
    }

    internal static object? ConvertValue(object value, Type targetType)
    {
        if (value is DBNull)
        {
            return Nullable.GetUnderlyingType(targetType) is not null || !targetType.IsValueType
                ? null
                : throw new InvalidOperationException($"Cannot map NULL to {targetType.Name}.");
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsInstanceOfType(value))
        {
            return value;
        }

        if (underlying.IsEnum)
        {
            return value is string name
                ? Enum.Parse(underlying, name, ignoreCase: true)
                : Enum.ToObject(underlying, value);
        }

        if (underlying == typeof(Guid))
        {
            return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
    }

    private sealed class ScalarMapper(Type type) : IRowMapper<T>
    {
        public T Map(DbDataReader reader) =>
            (T)ConvertValue(reader.GetValue(0), type)!;
    }

    private sealed class MutableMapper(
        Func<T> factory,
        PropertyHydrationOperation<T>[] operations) : IRowMapper<T>
    {
        public T Map(DbDataReader reader)
        {
            var instance = factory();
            for (var index = 0; index < operations.Length; index++)
            {
                operations[index].Apply(reader, instance);
            }

            return instance;
        }
    }

    private sealed class ConstructorMapper(
        ConstructorInfo constructor,
        ConstructorBinding[] constructorBindings,
        PropertyHydrationOperation<T>[] propertyOperations) : IRowMapper<T>
    {
        public T Map(DbDataReader reader)
        {
            var arguments = new object?[constructorBindings.Length];
            for (var index = 0; index < constructorBindings.Length; index++)
            {
                var binding = constructorBindings[index];
                arguments[index] = binding.Ordinal is { } ordinal
                    ? ConvertValue(reader.GetValue(ordinal), binding.Parameter.ParameterType)
                    : binding.Parameter.DefaultValue;
            }

            var instance = (T)constructor.Invoke(arguments);
            for (var index = 0; index < propertyOperations.Length; index++)
            {
                propertyOperations[index].Apply(reader, instance);
            }

            return instance;
        }
    }

    private sealed record ConstructorBinding(ParameterInfo Parameter, int? Ordinal);

}
