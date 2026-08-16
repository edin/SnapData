namespace SnapData;

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
