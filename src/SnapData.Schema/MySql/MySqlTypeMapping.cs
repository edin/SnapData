using System.Data;

namespace SnapData.Schema;

internal static class MySqlTypeMapping
{
    internal static (DbType? DbType, Type? ClrType) Resolve(
        string dataType,
        string columnType)
    {
        var unsigned = columnType.Contains("unsigned", StringComparison.OrdinalIgnoreCase);
        return dataType.ToLowerInvariant() switch
        {
            "bigint" when unsigned => (DbType.UInt64, typeof(ulong)),
            "bigint" => (DbType.Int64, typeof(long)),
            "int" or "integer" or "mediumint" when unsigned =>
                (DbType.UInt32, typeof(uint)),
            "int" or "integer" or "mediumint" => (DbType.Int32, typeof(int)),
            "smallint" when unsigned => (DbType.UInt16, typeof(ushort)),
            "smallint" => (DbType.Int16, typeof(short)),
            "tinyint" when columnType.StartsWith("tinyint(1)", StringComparison.OrdinalIgnoreCase) =>
                (DbType.Boolean, typeof(bool)),
            "tinyint" when unsigned => (DbType.Byte, typeof(byte)),
            "tinyint" => (DbType.SByte, typeof(sbyte)),
            "bit" => (DbType.UInt64, typeof(ulong)),
            "decimal" or "numeric" => (DbType.Decimal, typeof(decimal)),
            "double" => (DbType.Double, typeof(double)),
            "float" => (DbType.Single, typeof(float)),
            "date" => (DbType.Date, typeof(DateOnly)),
            "datetime" or "timestamp" => (DbType.DateTime, typeof(DateTime)),
            "time" => (DbType.Time, typeof(TimeSpan)),
            "year" => (DbType.Int32, typeof(int)),
            "char" or "varchar" or "tinytext" or "text" or "mediumtext" or "longtext"
                or "enum" or "set" or "json" => (DbType.String, typeof(string)),
            "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" =>
                (DbType.Binary, typeof(byte[])),
            "geometry" or "point" or "linestring" or "polygon"
                or "multipoint" or "multilinestring" or "multipolygon"
                or "geometrycollection" => (DbType.Object, typeof(object)),
            _ => (null, null)
        };
    }
}
