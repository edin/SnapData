using System.Data;

namespace SnapData.Schema;

public sealed record ColumnSchema
{
    public ColumnSchema(
        string name,
        int ordinal,
        string storeType,
        DbType? dbType,
        Type? clrType,
        bool isNullable,
        SchemaGeneratedKind generatedKind = SchemaGeneratedKind.None,
        string? defaultExpression = null,
        SchemaTypeAffinity? affinity = null,
        bool isAutoIncrement = false)
    {
        Name = SchemaModelGuard.RequiredName(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentNullException.ThrowIfNull(storeType);
        Ordinal = ordinal;
        StoreType = storeType;
        DbType = dbType;
        ClrType = clrType;
        IsNullable = isNullable;
        GeneratedKind = generatedKind;
        DefaultExpression = defaultExpression;
        Affinity = affinity;
        IsAutoIncrement = isAutoIncrement;
    }

    public string Name { get; }

    public int Ordinal { get; }

    public string StoreType { get; }

    public DbType? DbType { get; }

    public Type? ClrType { get; }

    public bool IsNullable { get; }

    public SchemaGeneratedKind GeneratedKind { get; }

    public string? DefaultExpression { get; }

    public SchemaTypeAffinity? Affinity { get; }

    public bool IsAutoIncrement { get; }
}

public enum SchemaGeneratedKind
{
    None,
    Identity,
    ComputedVirtual,
    ComputedStored,
    RowVersion,
    Hidden
}

public enum SchemaTypeAffinity
{
    Integer,
    Text,
    Real,
    Blob,
    Numeric
}
