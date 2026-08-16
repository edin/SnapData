using System.Data;
using System.Collections;
using System.Net;

namespace SnapData.Schema;

internal static class PostgresTypeMapping
{
    internal static (DbType? DbType, Type? ClrType) Resolve(
        string typeName,
        string typeCategory,
        string? elementTypeName)
    {
        if (typeCategory == "A" && elementTypeName is not null)
        {
            var element = Resolve(elementTypeName, "", null);
            return (DbType.Object, element.ClrType?.MakeArrayType());
        }

        if (typeCategory == "E")
        {
            return (DbType.String, typeof(string));
        }

        return typeName switch
        {
            "int8" => (DbType.Int64, typeof(long)),
            "int4" => (DbType.Int32, typeof(int)),
            "int2" => (DbType.Int16, typeof(short)),
            "bool" => (DbType.Boolean, typeof(bool)),
            "numeric" or "money" => (DbType.Decimal, typeof(decimal)),
            "float8" => (DbType.Double, typeof(double)),
            "float4" => (DbType.Single, typeof(float)),
            "text" or "varchar" or "bpchar" or "name" or "json" or "jsonb" =>
                (DbType.String, typeof(string)),
            "xml" => (DbType.Xml, typeof(string)),
            "bytea" => (DbType.Binary, typeof(byte[])),
            "uuid" => (DbType.Guid, typeof(Guid)),
            "date" => (DbType.Date, typeof(DateOnly)),
            "time" or "timetz" => (DbType.Time, typeof(TimeOnly)),
            "timestamp" or "timestamptz" => (DbType.DateTime, typeof(DateTime)),
            "interval" => (DbType.Time, typeof(TimeSpan)),
            "inet" or "cidr" => (DbType.Object, typeof(IPAddress)),
            "bit" or "varbit" => (DbType.Object, typeof(BitArray)),
            "oid" => (DbType.UInt32, typeof(uint)),
            _ => (null, null)
        };
    }
}
