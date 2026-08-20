namespace SnapData.Migrations.Cli.Presentation.Sql;

internal static class SqlLanguage
{
    public static readonly HashSet<string> Keywords = new(
        [
            "ADD", "ALTER", "AS", "ASC", "BY", "CASCADE", "CASE", "CHECK",
            "COLUMN", "CONSTRAINT", "CREATE", "CROSS", "CURRENT", "DEFAULT",
            "DELETE", "DESC", "DISTINCT", "DROP", "ELSE", "END", "EXISTS",
            "FIRST", "FOR", "FOREIGN", "FROM", "FULL", "GENERATED", "GROUP",
            "HAVING", "IDENTITY", "IF", "IN", "INDEX", "INNER", "INSERT",
            "INTO", "IS", "JOIN", "KEY", "LEFT", "MODIFY", "NOT", "NULL",
            "ON", "OR", "ORDER", "OUTER", "PRIMARY", "PROCEDURE", "REFERENCES",
            "RENAME", "REPLACE", "RESTRICT", "RIGHT", "SELECT", "SEQUENCE",
            "SET", "TABLE", "THEN", "TO", "TRIGGER", "UNIQUE", "UPDATE",
            "USING", "VALUES", "VIEW", "WHEN", "WHERE", "WITH", "WITHOUT"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static readonly HashSet<string> Types = new(
        [
            "BIGINT", "BINARY", "BIT", "BLOB", "BOOLEAN", "BYTEA", "CHAR",
            "CLOB", "DATE", "DATETIME", "DATETIME2", "DECIMAL", "DOUBLE", "ENUM",
            "FLOAT", "INT", "INTEGER", "JSON", "JSONB", "LONGTEXT", "MEDIUMINT",
            "MEDIUMTEXT", "NCHAR", "NTEXT", "NUMERIC", "NVARCHAR", "REAL",
            "SMALLINT", "TEXT", "TIME", "TIMESTAMP", "TINYINT", "UNIQUEIDENTIFIER",
            "UUID", "VARBINARY", "VARCHAR"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static readonly HashSet<string> Functions = new(
        [
            "ABS", "AVG", "CAST", "CEIL", "CEILING", "COALESCE", "CONCAT",
            "CONVERT", "COUNT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
            "DATEADD", "DATEDIFF", "EXTRACT", "FLOOR", "GROUP_CONCAT", "IFNULL",
            "IIF", "ISNULL", "JSON_EXTRACT", "JSON_VALUE", "LENGTH", "LOWER", "LTRIM",
            "MAX", "MIN", "NEWID", "NOW", "NULLIF", "POSITION", "POWER", "REPLACE",
            "ROUND", "RTRIM", "SQRT", "STRING_AGG", "SUBSTRING", "SUM", "TO_CHAR",
            "TO_DATE", "TRIM", "UPPER"
        ],
        StringComparer.OrdinalIgnoreCase);
}
