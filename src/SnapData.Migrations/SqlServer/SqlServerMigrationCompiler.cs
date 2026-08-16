namespace SnapData.Migrations;

public sealed class SqlServerMigrationCompiler : RelationalMigrationCompiler
{
    protected override string ProviderName => "SQL Server";

    protected override string IdentityClause => "IDENTITY(1,1)";

    protected override string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    protected override string StoreType(ColumnDefinition column) => column.Type switch
    {
        MigrationColumnType.Int16 => "SMALLINT",
        MigrationColumnType.Int32 => "INT",
        MigrationColumnType.Int64 => "BIGINT",
        MigrationColumnType.String => $"NVARCHAR({column.Length ?? 255})",
        MigrationColumnType.Text or MigrationColumnType.Json => "NVARCHAR(MAX)",
        MigrationColumnType.Boolean => "BIT",
        MigrationColumnType.Decimal => $"DECIMAL({column.Precision ?? 18},{column.Scale ?? 2})",
        MigrationColumnType.Float => "REAL",
        MigrationColumnType.Double => "FLOAT",
        MigrationColumnType.Guid => "UNIQUEIDENTIFIER",
        MigrationColumnType.Binary => "VARBINARY(MAX)",
        MigrationColumnType.Date => "DATE",
        MigrationColumnType.Time => "TIME",
        MigrationColumnType.DateTime => "DATETIME2",
        MigrationColumnType.DateTimeOffset => "DATETIMEOFFSET",
        _ => throw new ArgumentOutOfRangeException(nameof(column.Type), column.Type, null)
    };

    protected override string BooleanLiteral(bool value) => value ? "1" : "0";

    protected override string ReferentialActionSql(SnapData.Schema.ReferentialAction action) =>
        action == SnapData.Schema.ReferentialAction.Restrict
            ? "NO ACTION"
            : base.ReferentialActionSql(action);

    protected override string BinaryLiteral(byte[] value) => $"0x{Convert.ToHexString(value)}";

    protected override string CompileRenameColumn(RenameColumnOperation operation)
    {
        var qualifiedColumn = $"{QuoteTable(operation.Table)}.{QuoteIdentifier(operation.Column)}";
        return $"EXEC sp_rename N'{qualifiedColumn.Replace("'", "''")}', N'{operation.NewName.Replace("'", "''")}', N'COLUMN'";
    }
}
