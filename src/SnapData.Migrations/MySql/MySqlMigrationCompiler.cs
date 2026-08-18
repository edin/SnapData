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

    protected override IEnumerable<string> CompileAlterColumn(AlterColumnOperation operation)
    {
        EnsureAlterColumnHasNoConstraintChanges(operation.Column);
        yield return $"ALTER TABLE {QuoteTable(operation.Table)} MODIFY COLUMN {CompileColumn(operation.Column)}";
    }

    protected override string CompileDropIndex(DropIndexOperation operation) =>
        $"DROP INDEX {QuoteIdentifier(operation.Index)} ON {QuoteTable(operation.Table)}";

    protected override string CompileDropForeignKey(DropForeignKeyOperation operation) =>
        $"ALTER TABLE {QuoteTable(operation.Table)} DROP FOREIGN KEY {QuoteIdentifier(operation.ForeignKey)}";

    protected override string CompileDropCheck(DropCheckConstraintOperation operation) =>
        $"ALTER TABLE {QuoteTable(operation.Table)} DROP CHECK {QuoteIdentifier(operation.Check)}";

    protected override string CompileRenameTable(RenameTableOperation operation) =>
        $"RENAME TABLE {QuoteTable(operation.Table)} TO {QuoteIdentifier(RenameTarget(operation.NewName))}";

    protected override IEnumerable<string> CompileCreateTableIfNotExists(
        CreateTableOperation operation,
        string createTableSql,
        IReadOnlyList<string> indexSql)
    {
        var definitions = operation.Indexes.Select(index =>
        {
            var name = index.Name ?? DefaultIndexName(operation.Table, index);
            var columns = string.Join(", ", index.Columns.Select(column =>
                $"{QuoteIdentifier(column.Name)} {(column.Order == MigrationSortOrder.Descending ? "DESC" : "ASC")}"));
            return $"{(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX {QuoteIdentifier(name)} ({columns})";
        }).ToArray();
        var suffix = definitions.Length == 0
            ? string.Empty
            : "," + Environment.NewLine + "    " + string.Join(
                "," + Environment.NewLine + "    ", definitions);
        yield return createTableSql[..^1].TrimEnd('\r', '\n')
            .Replace(
                "CREATE TABLE ",
                "CREATE TABLE IF NOT EXISTS ",
                StringComparison.Ordinal) + suffix + Environment.NewLine + ")";
    }
}
