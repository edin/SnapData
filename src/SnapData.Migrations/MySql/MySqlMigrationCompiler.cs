namespace SnapData.Migrations;

public sealed class MySqlMigrationCompiler : RelationalMigrationCompiler
{
    protected override string ProviderName => "MySQL";

    protected override string IdentityClause => "AUTO_INCREMENT";

    protected override string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
    }

    protected override string StoreType(ColumnDefinition column) => column.Type switch
    {
        MigrationColumnType.Int16 => "SMALLINT",
        MigrationColumnType.Int32 => "INT",
        MigrationColumnType.Int64 => "BIGINT",
        MigrationColumnType.String => $"VARCHAR({column.Length ?? 255})",
        MigrationColumnType.Text => "LONGTEXT",
        MigrationColumnType.Boolean => "BOOLEAN",
        MigrationColumnType.Decimal => $"DECIMAL({column.Precision ?? 18},{column.Scale ?? 2})",
        MigrationColumnType.Float => "FLOAT",
        MigrationColumnType.Double => "DOUBLE",
        MigrationColumnType.Guid => "CHAR(36)",
        MigrationColumnType.Binary => "LONGBLOB",
        MigrationColumnType.Date => "DATE",
        MigrationColumnType.Time => "TIME",
        MigrationColumnType.DateTime or MigrationColumnType.DateTimeOffset => "DATETIME(6)",
        MigrationColumnType.Json => "JSON",
        _ => throw new ArgumentOutOfRangeException(nameof(column.Type), column.Type, null)
    };

    protected override string BooleanLiteral(bool value) => value ? "TRUE" : "FALSE";

    protected override string BinaryLiteral(byte[] value) => $"X'{Convert.ToHexString(value)}'";
}
