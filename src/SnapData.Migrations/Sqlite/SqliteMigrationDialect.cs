using SnapData.Schema;

namespace SnapData.Migrations;

public sealed class SqliteMigrationDialect : IMigrationDialect
{
    public static SqliteMigrationDialect Instance { get; } = new();

    private SqliteMigrationDialect()
    {
    }

    public string ProviderName => Provider.Sqlite;

    public IMigrationCompiler Compiler { get; } = new SqliteMigrationCompiler();

    public IMigrationLock MigrationLock => SqliteMigrationLock.Instance;

    public ISchemaInspector CreateSchemaInspector(IDbExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        return new SqliteSchemaInspector(executor);
    }

    public string QuoteIdentifier(string identifier) =>
        $"\"{Required(identifier).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    public string QuoteTable(string table) => QuoteQualified(table);

    public string CreateHistoryTableSql(string table) =>
        $"CREATE TABLE {QuoteTable(table)} ({QuoteIdentifier("migration_id")} VARCHAR(250) NOT NULL PRIMARY KEY, {QuoteIdentifier("applied_order")} BIGINT NOT NULL UNIQUE, {QuoteIdentifier("applied_at")} VARCHAR(40) NOT NULL, {QuoteIdentifier("fingerprint")} CHAR(64) NULL)";

    private string QuoteQualified(string value) =>
        string.Join(".", Required(value).Split('.').Select(QuoteIdentifier));

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty identifier is required.", nameof(value))
            : value;
}
