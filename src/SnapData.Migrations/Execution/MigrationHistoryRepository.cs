namespace SnapData.Migrations;

internal sealed class MigrationHistoryRepository(string table, IMigrationDialect dialect)
{
    private readonly string quotedTable = dialect.QuoteTable(table);
    private readonly string quotedMigrationId = dialect.QuoteIdentifier("migration_id");
    private readonly string quotedAppliedAt = dialect.QuoteIdentifier("applied_at");

    public async Task EnsureCreatedAsync(
        IDbExecutor executor,
        CancellationToken cancellationToken)
    {
        var inspector = dialect.CreateSchemaInspector(executor);
        if (!await inspector.TableExistsAsync(ParseName(table), cancellationToken))
        {
            await executor.ExecuteAsync(
                dialect.CreateHistoryTableSql(table),
                cancellationToken: cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MigrationHistoryEntry>> ReadAsync(
        IDbExecutor executor,
        CancellationToken cancellationToken)
    {
        var rows = await executor.QueryAsync<HistoryRow>(
            $"SELECT {quotedMigrationId} AS MigrationId, {quotedAppliedAt} AS AppliedAt FROM {quotedTable} ORDER BY {quotedMigrationId}",
            cancellationToken: cancellationToken);
        return rows.Select(row => new MigrationHistoryEntry(
            row.MigrationId,
            DateTimeOffset.Parse(row.AppliedAt, System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
    }

    public Task InsertAsync(
        IDbExecutor executor,
        string migrationId,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync(
            $"INSERT INTO {quotedTable} ({quotedMigrationId}, {quotedAppliedAt}) VALUES (@migrationId, @appliedAt)",
            new
            {
                migrationId,
                appliedAt = appliedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken);

    public Task DeleteAsync(
        IDbExecutor executor,
        string migrationId,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync(
            $"DELETE FROM {quotedTable} WHERE {quotedMigrationId} = @migrationId",
            new { migrationId },
            cancellationToken: cancellationToken);

    private static SnapData.Schema.SchemaObjectName ParseName(string value)
    {
        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => new SnapData.Schema.SchemaObjectName(parts[0]),
            2 => new SnapData.Schema.SchemaObjectName(parts[1], parts[0]),
            _ => throw new ArgumentException(
                "A history table must use 'table' or 'schema.table' form.", nameof(value))
        };
    }

    private sealed class HistoryRow
    {
        public string MigrationId { get; set; } = string.Empty;

        public string AppliedAt { get; set; } = string.Empty;
    }
}

public sealed record MigrationHistoryEntry(string MigrationId, DateTimeOffset AppliedAt);
