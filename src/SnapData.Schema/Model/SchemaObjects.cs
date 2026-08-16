namespace SnapData.Schema;

public sealed record SchemaObjectName
{
    public SchemaObjectName(string name, string? schema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (schema is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        }

        Name = name;
        Schema = schema;
    }

    public string Name { get; }

    public string? Schema { get; }

    public override string ToString() => Schema is null ? Name : $"{Schema}.{Name}";
}

public enum SchemaObjectKind
{
    Table,
    View,
    Procedure,
    Function,
    Sequence,
    Trigger
}

public sealed record SchemaObjectInfo(
    SchemaObjectName Name,
    SchemaObjectKind Kind,
    bool IsSystemObject = false);
