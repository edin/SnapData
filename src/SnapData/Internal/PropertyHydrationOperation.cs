using System.Data.Common;
using System.Reflection;

namespace SnapData;

internal abstract class PropertyHydrationOperation<TEntity>
{
    private static readonly MethodInfo CreateFallbackMethod = typeof(PropertyHydrationOperation<TEntity>)
        .GetMethod(nameof(CreateFallback), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CreateNullableMethod = typeof(PropertyHydrationOperation<TEntity>)
        .GetMethod(nameof(CreateNullable), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CreateNullableReferenceMethod = typeof(PropertyHydrationOperation<TEntity>)
        .GetMethod(nameof(CreateNullableReference), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal abstract void Apply(DbDataReader reader, TEntity entity);

    internal static PropertyHydrationOperation<TEntity> Create(
        PropertyMapping property,
        int ordinal,
        Type fieldType)
    {
        var propertyType = property.PropertyType;
        if (propertyType == fieldType)
        {
            if (!propertyType.IsValueType && property.IsNullable)
            {
                return (PropertyHydrationOperation<TEntity>)CreateNullableReferenceMethod
                    .MakeGenericMethod(propertyType)
                    .Invoke(null, [property, ordinal])!;
            }

            if (propertyType == typeof(long))
            {
                return new Int64Operation(ordinal, Setter<long>(property));
            }

            if (propertyType == typeof(int))
            {
                return new Int32Operation(ordinal, Setter<int>(property));
            }

            if (propertyType == typeof(short))
            {
                return new Int16Operation(ordinal, Setter<short>(property));
            }

            if (propertyType == typeof(bool))
            {
                return new BooleanOperation(ordinal, Setter<bool>(property));
            }

            if (propertyType == typeof(string))
            {
                return new StringOperation(ordinal, Setter<string>(property));
            }

            if (propertyType == typeof(decimal))
            {
                return new DecimalOperation(ordinal, Setter<decimal>(property));
            }

            if (propertyType == typeof(double))
            {
                return new DoubleOperation(ordinal, Setter<double>(property));
            }

            if (propertyType == typeof(float))
            {
                return new SingleOperation(ordinal, Setter<float>(property));
            }

            if (propertyType == typeof(DateTime))
            {
                return new DateTimeOperation(ordinal, Setter<DateTime>(property));
            }

            if (propertyType == typeof(Guid))
            {
                return new GuidOperation(ordinal, Setter<Guid>(property));
            }

            if (propertyType == typeof(byte[]))
            {
                return new BytesOperation(ordinal, Setter<byte[]>(property));
            }
        }

        var nullableType = Nullable.GetUnderlyingType(propertyType);
        if (nullableType == fieldType)
        {
            return (PropertyHydrationOperation<TEntity>)CreateNullableMethod
                .MakeGenericMethod(nullableType)
                .Invoke(null, [property, ordinal])!;
        }

        return (PropertyHydrationOperation<TEntity>)CreateFallbackMethod
            .MakeGenericMethod(propertyType)
            .Invoke(null, [property, ordinal])!;
    }

    private static PropertyHydrationOperation<TEntity> CreateNullable<TValue>(
        PropertyMapping property,
        int ordinal)
        where TValue : struct =>
        new NullableOperation<TValue>(ordinal, Setter<TValue?>(property));

    private static PropertyHydrationOperation<TEntity> CreateFallback<TValue>(
        PropertyMapping property,
        int ordinal) =>
        new ConvertingOperation<TValue>(ordinal, Setter<TValue>(property));

    private static PropertyHydrationOperation<TEntity> CreateNullableReference<TValue>(
        PropertyMapping property,
        int ordinal)
        where TValue : class =>
        new NullableReferenceOperation<TValue>(ordinal, Setter<TValue?>(property));

    private static Action<TEntity, TValue> Setter<TValue>(PropertyMapping property) =>
        property.Property.SetMethod?.CreateDelegate<Action<TEntity, TValue>>()
        ?? throw new InvalidOperationException(
            $"Property {typeof(TEntity).Name}.{property.PropertyName} is not writable.");

    private sealed class Int64Operation(int ordinal, Action<TEntity, long> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetInt64(ordinal));
    }

    private sealed class Int32Operation(int ordinal, Action<TEntity, int> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetInt32(ordinal));
    }

    private sealed class Int16Operation(int ordinal, Action<TEntity, short> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetInt16(ordinal));
    }

    private sealed class BooleanOperation(int ordinal, Action<TEntity, bool> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetBoolean(ordinal));
    }

    private sealed class StringOperation(int ordinal, Action<TEntity, string> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetString(ordinal));
    }

    private sealed class DecimalOperation(int ordinal, Action<TEntity, decimal> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetDecimal(ordinal));
    }

    private sealed class DoubleOperation(int ordinal, Action<TEntity, double> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetDouble(ordinal));
    }

    private sealed class SingleOperation(int ordinal, Action<TEntity, float> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetFloat(ordinal));
    }

    private sealed class DateTimeOperation(int ordinal, Action<TEntity, DateTime> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetDateTime(ordinal));
    }

    private sealed class GuidOperation(int ordinal, Action<TEntity, Guid> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetGuid(ordinal));
    }

    private sealed class BytesOperation(int ordinal, Action<TEntity, byte[]> setter)
        : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(entity, reader.GetFieldValue<byte[]>(ordinal));
    }

    private sealed class NullableOperation<TValue>(
        int ordinal,
        Action<TEntity, TValue?> setter) : PropertyHydrationOperation<TEntity>
        where TValue : struct
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(
                entity,
                reader.IsDBNull(ordinal)
                    ? null
                    : reader.GetFieldValue<TValue>(ordinal));
    }

    private sealed class NullableReferenceOperation<TValue>(
        int ordinal,
        Action<TEntity, TValue?> setter) : PropertyHydrationOperation<TEntity>
        where TValue : class
    {
        internal override void Apply(DbDataReader reader, TEntity entity) =>
            setter(
                entity,
                reader.IsDBNull(ordinal)
                    ? null
                    : reader.GetFieldValue<TValue>(ordinal));
    }

    private sealed class ConvertingOperation<TValue>(
        int ordinal,
        Action<TEntity, TValue> setter) : PropertyHydrationOperation<TEntity>
    {
        internal override void Apply(DbDataReader reader, TEntity entity)
        {
            var value = RowMapper<TEntity>.ConvertValue(
                reader.GetValue(ordinal),
                typeof(TValue));
            setter(entity, (TValue)value!);
        }
    }
}
