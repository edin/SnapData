using System.Data;

namespace SnapData.Schema;

internal static class SqliteTypeMapping
{
    internal static SqliteTypeInfo Resolve(string declaredType)
    {
        ArgumentNullException.ThrowIfNull(declaredType);
        var type = declaredType.ToUpperInvariant();
        if (type.Contains("INT", StringComparison.Ordinal))
        {
            return new SqliteTypeInfo(SchemaTypeAffinity.Integer, DbType.Int64, typeof(long));
        }

        if (type.Contains("CHAR", StringComparison.Ordinal)
            || type.Contains("CLOB", StringComparison.Ordinal)
            || type.Contains("TEXT", StringComparison.Ordinal))
        {
            return new SqliteTypeInfo(SchemaTypeAffinity.Text, DbType.String, typeof(string));
        }

        if (type.Length == 0 || type.Contains("BLOB", StringComparison.Ordinal))
        {
            return new SqliteTypeInfo(SchemaTypeAffinity.Blob, DbType.Binary, typeof(byte[]));
        }

        if (type.Contains("REAL", StringComparison.Ordinal)
            || type.Contains("FLOA", StringComparison.Ordinal)
            || type.Contains("DOUB", StringComparison.Ordinal))
        {
            return new SqliteTypeInfo(SchemaTypeAffinity.Real, DbType.Double, typeof(double));
        }

        return new SqliteTypeInfo(SchemaTypeAffinity.Numeric, DbType.Decimal, typeof(decimal));
    }
}

internal sealed record SqliteTypeInfo(
    SchemaTypeAffinity Affinity,
    DbType DbType,
    Type ClrType);
