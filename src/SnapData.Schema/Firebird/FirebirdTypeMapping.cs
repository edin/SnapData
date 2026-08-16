using System.Data;

namespace SnapData.Schema;

internal static class FirebirdTypeMapping
{
    internal static FirebirdTypeInfo Resolve(
        int fieldType,
        int fieldSubType,
        int length,
        int? characterLength,
        int? precision,
        int scale,
        string? characterSet)
    {
        if (fieldSubType is 1 or 2 && fieldType is 7 or 8 or 16 or 26)
        {
            var name = fieldSubType == 1 ? "NUMERIC" : "DECIMAL";
            var actualPrecision = precision ?? fieldType switch
            {
                7 => 4,
                8 => 9,
                16 => 18,
                26 => 38,
                _ => 18
            };
            return new FirebirdTypeInfo(
                $"{name}({actualPrecision},{Math.Abs(scale)})",
                DbType.Decimal,
                typeof(decimal));
        }

        return fieldType switch
        {
            7 => new("SMALLINT", DbType.Int16, typeof(short)),
            8 => new("INTEGER", DbType.Int32, typeof(int)),
            10 => new("FLOAT", DbType.Single, typeof(float)),
            12 => new("DATE", DbType.Date, typeof(DateOnly)),
            13 => new("TIME", DbType.Time, typeof(TimeOnly)),
            14 => CharacterType("CHAR", length, characterLength, characterSet),
            16 => new("BIGINT", DbType.Int64, typeof(long)),
            23 => new("BOOLEAN", DbType.Boolean, typeof(bool)),
            24 => new("DECFLOAT(16)", DbType.Object, typeof(object)),
            25 => new("DECFLOAT(34)", DbType.Object, typeof(object)),
            26 => new("INT128", DbType.Object, typeof(Int128)),
            27 => new("DOUBLE PRECISION", DbType.Double, typeof(double)),
            28 => new("TIME WITH TIME ZONE", DbType.Object, typeof(object)),
            29 => new("TIMESTAMP WITH TIME ZONE", DbType.Object, typeof(object)),
            35 => new("TIMESTAMP", DbType.DateTime, typeof(DateTime)),
            37 => CharacterType("VARCHAR", length, characterLength, characterSet),
            261 when fieldSubType == 1 => new("BLOB SUB_TYPE TEXT", DbType.String, typeof(string)),
            261 => new("BLOB SUB_TYPE BINARY", DbType.Binary, typeof(byte[])),
            _ => new($"TYPE {fieldType}", null, null)
        };
    }

    private static FirebirdTypeInfo CharacterType(
        string name,
        int length,
        int? characterLength,
        string? characterSet)
    {
        var size = characterLength ?? length;
        return characterSet?.Trim().Equals("OCTETS", StringComparison.OrdinalIgnoreCase) == true
            ? new FirebirdTypeInfo($"{name}({size}) CHARACTER SET OCTETS", DbType.Binary, typeof(byte[]))
            : new FirebirdTypeInfo($"{name}({size})", DbType.String, typeof(string));
    }
}

internal sealed record FirebirdTypeInfo(string StoreType, DbType? DbType, Type? ClrType);
