namespace SnapData;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TableAttribute : Attribute
{
    public TableAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public string? Schema { get; init; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ColumnAttribute : Attribute
{
    public ColumnAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class KeyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class IgnoreAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class RelationAttribute(string localKey, string foreignKey) : Attribute
{
    public string LocalKey { get; } = string.IsNullOrWhiteSpace(localKey)
        ? throw new ArgumentException("A relation local key is required.", nameof(localKey))
        : localKey;

    public string ForeignKey { get; } = string.IsNullOrWhiteSpace(foreignKey)
        ? throw new ArgumentException("A relation foreign key is required.", nameof(foreignKey))
        : foreignKey;
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class GeneratedAttribute(GeneratedKind kind) : Attribute
{
    public GeneratedKind Kind { get; } = kind;
}

public enum GeneratedKind
{
    Never,
    Identity,
    Computed
}
