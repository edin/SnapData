using SnapData.Schema;

namespace SnapData.Migrations;

public interface IMigrationDialect
{
    string ProviderName { get; }

    IMigrationCompiler Compiler { get; }

    IMigrationLock? MigrationLock { get; }

    ISchemaInspector CreateSchemaInspector(IDbExecutor executor);

    string QuoteIdentifier(string identifier);

    string QuoteTable(string table);

    string CreateHistoryTableSql(string table);
}
