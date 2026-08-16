using SnapData.Schema;

namespace SnapData.Migrations;

public sealed class SqlServerMigrationDialect : IMigrationDialect
{
    public static SqlServerMigrationDialect Instance { get; } = new();
    private SqlServerMigrationDialect() { }
    public IMigrationCompiler Compiler { get; } = new SqlServerMigrationCompiler();
    public IMigrationLock MigrationLock => SqlServerMigrationLock.Instance;
    public ISchemaInspector CreateSchemaInspector(IDbExecutor executor) =>
        new SqlServerSchemaInspector(executor ?? throw new ArgumentNullException(nameof(executor)));
    public string QuoteIdentifier(string value) => $"[{Required(value).Replace("]", "]]", StringComparison.Ordinal)}]";
    public string QuoteTable(string value) => Qualified(value, QuoteIdentifier);
    public string CreateHistoryTableSql(string table) =>
        $"CREATE TABLE {QuoteTable(table)} ({QuoteIdentifier("migration_id")} NVARCHAR(250) NOT NULL PRIMARY KEY, {QuoteIdentifier("applied_at")} NVARCHAR(40) NOT NULL)";

    private static string Required(string value) => MigrationDialectNames.Required(value);
    private static string Qualified(string value, Func<string, string> quote) =>
        MigrationDialectNames.Qualified(value, quote);
}

public sealed class PostgresMigrationDialect : IMigrationDialect
{
    public static PostgresMigrationDialect Instance { get; } = new();
    private PostgresMigrationDialect() { }
    public IMigrationCompiler Compiler { get; } = new PostgresMigrationCompiler();
    public IMigrationLock MigrationLock => PostgresMigrationLock.Instance;
    public ISchemaInspector CreateSchemaInspector(IDbExecutor executor) =>
        new PostgresSchemaInspector(executor ?? throw new ArgumentNullException(nameof(executor)));
    public string QuoteIdentifier(string value) => $"\"{MigrationDialectNames.Required(value).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    public string QuoteTable(string value) => MigrationDialectNames.Qualified(value, QuoteIdentifier);
    public string CreateHistoryTableSql(string table) =>
        $"CREATE TABLE {QuoteTable(table)} ({QuoteIdentifier("migration_id")} VARCHAR(250) NOT NULL PRIMARY KEY, {QuoteIdentifier("applied_at")} VARCHAR(40) NOT NULL)";
}

public sealed class MySqlMigrationDialect : IMigrationDialect
{
    public static MySqlMigrationDialect Instance { get; } = new();
    private MySqlMigrationDialect() { }
    public IMigrationCompiler Compiler { get; } = new MySqlMigrationCompiler();
    public IMigrationLock MigrationLock => MySqlMigrationLock.Instance;
    public ISchemaInspector CreateSchemaInspector(IDbExecutor executor) =>
        new MySqlSchemaInspector(executor ?? throw new ArgumentNullException(nameof(executor)));
    public string QuoteIdentifier(string value) => $"`{MigrationDialectNames.Required(value).Replace("`", "``", StringComparison.Ordinal)}`";
    public string QuoteTable(string value) => MigrationDialectNames.Qualified(value, QuoteIdentifier);
    public string CreateHistoryTableSql(string table) =>
        $"CREATE TABLE {QuoteTable(table)} ({QuoteIdentifier("migration_id")} VARCHAR(250) NOT NULL PRIMARY KEY, {QuoteIdentifier("applied_at")} VARCHAR(40) NOT NULL)";
}

public sealed class FirebirdMigrationDialect : IMigrationDialect
{
    public static FirebirdMigrationDialect Instance { get; } = new();
    private FirebirdMigrationDialect() { }
    public IMigrationCompiler Compiler { get; } = new FirebirdMigrationCompiler();
    public IMigrationLock? MigrationLock => null;
    public ISchemaInspector CreateSchemaInspector(IDbExecutor executor) =>
        new FirebirdSchemaInspector(executor ?? throw new ArgumentNullException(nameof(executor)));
    public string QuoteIdentifier(string value) => $"\"{MigrationDialectNames.Required(value).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    public string QuoteTable(string value) => MigrationDialectNames.Qualified(value, QuoteIdentifier);
    public string CreateHistoryTableSql(string table) =>
        $"CREATE TABLE {QuoteTable(table)} ({QuoteIdentifier("migration_id")} VARCHAR(250) NOT NULL PRIMARY KEY, {QuoteIdentifier("applied_at")} VARCHAR(40) NOT NULL)";
}

internal static class MigrationDialectNames
{
    public static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty identifier is required.", nameof(value))
            : value;

    public static string Qualified(string value, Func<string, string> quote)
    {
        var parts = Required(value).Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A table name must use 'table' or 'schema.table' form.", nameof(value));
        }
        return string.Join(".", parts.Select(quote));
    }
}
