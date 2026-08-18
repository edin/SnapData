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

    protected override IEnumerable<string> CompileAlterColumn(AlterColumnOperation operation)
    {
        EnsureAlterColumnHasNoConstraintChanges(operation.Column);
        yield return
            $"ALTER TABLE {QuoteTable(operation.Table)} ALTER COLUMN " +
            $"{QuoteIdentifier(operation.Column.Name)} {StoreType(operation.Column)} " +
            (operation.Column.IsNullable ? "NULL" : "NOT NULL");
    }

    protected override IEnumerable<string> CompileSetColumnDefault(
        SetColumnDefaultOperation operation)
    {
        yield return CompileDropDefaultConstraint(operation.Table, operation.Column);
        yield return $"ALTER TABLE {QuoteTable(operation.Table)} ADD DEFAULT " +
            $"{FormatDefault(operation.Value)} FOR {QuoteIdentifier(operation.Column)}";
    }

    protected override IEnumerable<string> CompileDropColumnDefault(
        DropColumnDefaultOperation operation)
    {
        yield return CompileDropDefaultConstraint(operation.Table, operation.Column);
    }

    protected override string CompileRenameColumn(RenameColumnOperation operation)
    {
        var qualifiedColumn = $"{QuoteTable(operation.Table)}.{QuoteIdentifier(operation.Column)}";
        return $"EXEC sp_rename N'{qualifiedColumn.Replace("'", "''")}', N'{operation.NewName.Replace("'", "''")}', N'COLUMN'";
    }

    protected override string CompileRenameTable(RenameTableOperation operation)
    {
        var newName = RenameTarget(operation.NewName);
        var table = QuoteTable(operation.Table).Replace("'", "''", StringComparison.Ordinal);
        return $"EXEC sp_rename N'{table}', N'{newName.Replace("'", "''", StringComparison.Ordinal)}', N'OBJECT'";
    }

    protected override string CompileDropIndex(DropIndexOperation operation) =>
        $"DROP INDEX {QuoteIdentifier(operation.Index)} ON {QuoteTable(operation.Table)}";

    protected override IEnumerable<string> CompileCreateTableIfNotExists(
        CreateTableOperation operation,
        string createTableSql,
        IReadOnlyList<string> indexSql)
    {
        var objectName = QuoteTable(operation.Table).Replace("'", "''", StringComparison.Ordinal);
        var body = string.Join(
            Environment.NewLine,
            new[] { createTableSql }.Concat(indexSql)
                .Select(statement => "    " + statement.Replace(
                    Environment.NewLine,
                    Environment.NewLine + "    ",
                    StringComparison.Ordinal)));
        yield return $"IF OBJECT_ID(N'{objectName}', N'U') IS NULL{Environment.NewLine}BEGIN{Environment.NewLine}{body}{Environment.NewLine}END";
    }

    private string CompileDropDefaultConstraint(string tableName, string columnName)
    {
        var table = QuoteTable(tableName);
        var objectName = table.Replace("'", "''", StringComparison.Ordinal);
        var column = columnName.Replace("'", "''", StringComparison.Ordinal);
        return
            $"DECLARE @snapdata_default sysname;{Environment.NewLine}" +
            $"DECLARE @snapdata_sql nvarchar(max);{Environment.NewLine}" +
            $"SELECT @snapdata_default = dc.name{Environment.NewLine}" +
            $"FROM sys.default_constraints AS dc{Environment.NewLine}" +
            $"INNER JOIN sys.columns AS c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id{Environment.NewLine}" +
            $"WHERE dc.parent_object_id = OBJECT_ID(N'{objectName}') AND c.name = N'{column}';{Environment.NewLine}" +
            $"IF @snapdata_default IS NOT NULL{Environment.NewLine}" +
            $"BEGIN{Environment.NewLine}" +
            $"    SET @snapdata_sql = N'ALTER TABLE {objectName} DROP CONSTRAINT ' + QUOTENAME(@snapdata_default);{Environment.NewLine}" +
            $"    EXEC sys.sp_executesql @snapdata_sql;{Environment.NewLine}" +
            $"END";
    }
}
