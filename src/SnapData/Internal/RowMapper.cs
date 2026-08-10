using System.Data.Common;
using System.Globalization;
using System.Reflection;

namespace SnapData;

internal static class RowMapper<T>
{
    internal static Func<DbDataReader, T> Create(
        DbDataReader reader,
        IEntityMappingProvider mappingProvider)
    {
        var type = typeof(T);
        if (IsScalar(type))
        {
            return row => (T)ConvertValue(row.GetValue(0), type)!;
        }

        var mapping = mappingProvider.GetMapping(type);
        var columns = Enumerable.Range(0, reader.FieldCount)
            .ToDictionary(reader.GetName, StringComparer.OrdinalIgnoreCase);
        var constructor = SelectConstructor(type, mapping, columns);
        var constructorParameters = constructor?.GetParameters() ?? [];
        var constructorBindings = constructorParameters.Select(parameter =>
        {
            var property = mapping.FindProperty(parameter.Name!);
            var columnName = property?.ColumnName ?? parameter.Name!;
            return columns.TryGetValue(columnName, out var ordinal)
                ? new ConstructorBinding(parameter, ordinal)
                : parameter.HasDefaultValue
                    ? new ConstructorBinding(parameter, null)
                    : throw new InvalidOperationException(
                        $"Column '{columnName}' is required to construct {type.Name}.");
        }).ToArray();
        var propertyBindings = mapping.Properties
            .Where(property => property.CanWrite && columns.ContainsKey(property.ColumnName))
            .Select(property => new PropertyBinding(property, columns[property.ColumnName]))
            .ToArray();

        return row =>
        {
            object instance;
            if (constructor is not null && constructorParameters.Length > 0)
            {
                var arguments = constructorBindings.Select(binding =>
                    binding.Ordinal is { } ordinal
                        ? ConvertValue(row.GetValue(ordinal), binding.Parameter.ParameterType)
                        : binding.Parameter.DefaultValue).ToArray();
                instance = constructor.Invoke(arguments);
            }
            else
            {
                instance = Activator.CreateInstance(type, nonPublic: true)
                    ?? throw new InvalidOperationException(
                        $"{type.Name} needs a parameterless constructor or a constructor matching result columns.");
            }

            foreach (var binding in propertyBindings)
            {
                binding.Property.SetValue(
                    instance,
                    ConvertValue(row.GetValue(binding.Ordinal), binding.Property.PropertyType));
            }

            return (T)instance;
        };
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

    private sealed record ConstructorBinding(ParameterInfo Parameter, int? Ordinal);

    private sealed record PropertyBinding(PropertyMapping Property, int Ordinal);
}
