using System.Data;

namespace SnapData.Schema;

internal static class SqlServerTypeMapping
{
    internal static (DbType? DbType, Type? ClrType) Resolve(string storeType) =>
        storeType.ToLowerInvariant() switch
        {
            "bigint" => (DbType.Int64, typeof(long)),
            "int" => (DbType.Int32, typeof(int)),
            "smallint" => (DbType.Int16, typeof(short)),
            "tinyint" => (DbType.Byte, typeof(byte)),
            "bit" => (DbType.Boolean, typeof(bool)),
            "decimal" or "numeric" or "money" or "smallmoney" =>
                (DbType.Decimal, typeof(decimal)),
            "float" => (DbType.Double, typeof(double)),
            "real" => (DbType.Single, typeof(float)),
            "date" or "datetime" or "datetime2" or "smalldatetime" =>
                (DbType.DateTime, typeof(DateTime)),
            "datetimeoffset" => (DbType.DateTimeOffset, typeof(DateTimeOffset)),
            "time" => (DbType.Time, typeof(TimeSpan)),
            "uniqueidentifier" => (DbType.Guid, typeof(Guid)),
            "binary" or "varbinary" or "image" or "timestamp" or "rowversion" =>
                (DbType.Binary, typeof(byte[])),
            "char" or "varchar" or "text" => (DbType.AnsiString, typeof(string)),
            "nchar" or "nvarchar" or "ntext" or "xml" => (DbType.String, typeof(string)),
            "sql_variant" => (DbType.Object, typeof(object)),
            _ => (null, null)
        };
}
