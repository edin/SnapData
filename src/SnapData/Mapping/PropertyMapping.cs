using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace SnapData;

public sealed class PropertyMapping
{
    internal PropertyMapping(
        PropertyInfo property,
        string columnName,
        bool isNullable,
        bool isKey,
        DatabaseGeneratedOption generated)
    {
        Property = property;
        PropertyName = property.Name;
        ColumnName = columnName;
        Column = new ColumnReference(columnName);
        PropertyType = property.PropertyType;
        ValueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        IsNullable = isNullable;
        IsKey = isKey;
        Generated = generated;
        CanRead = property.GetMethod is not null;
        CanWrite = property.SetMethod is not null;
    }

    public PropertyInfo Property { get; }

    public string PropertyName { get; }

    public string ColumnName { get; }

    public ColumnReference Column { get; }

    public Type PropertyType { get; }

    public Type ValueType { get; }

    public bool IsNullable { get; }

    public bool IsKey { get; }

    public DatabaseGeneratedOption Generated { get; }

    public bool CanRead { get; }

    public bool CanWrite { get; }

    public bool IsEnum => ValueType.IsEnum;

    public bool IsGenerated => Generated != DatabaseGeneratedOption.None;

    public bool IsSelectable => true;

    public bool IsInsertable => CanRead && Generated == DatabaseGeneratedOption.None;

    public bool IsUpdatable =>
        CanRead && !IsKey && Generated == DatabaseGeneratedOption.None;

    public object? GetValue(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!CanRead)
        {
            throw new InvalidOperationException(
                $"Property {Property.DeclaringType?.Name}.{PropertyName} is not readable.");
        }

        return Property.GetValue(entity);
    }

    public void SetValue(object entity, object? value)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!CanWrite)
        {
            throw new InvalidOperationException(
                $"Property {Property.DeclaringType?.Name}.{PropertyName} is not writable.");
        }

        Property.SetValue(entity, value);
    }
}
