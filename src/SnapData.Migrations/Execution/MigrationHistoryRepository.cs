namespace SnapData.Migrations;

internal sealed class MigrationHistoryRepository(string table, IMigrationDialect dialect)
{
    private readonly string quotedTable = dialect.QuoteTable(table);
    private readonly string quotedMigrationId = dialect.QuoteIdentifier("migration_id");
    private readonly string quotedAppliedOrder = dialect.QuoteIdentifier("applied_order");
    private readonly string quotedAppliedAt = dialect.QuoteIdentifier("applied_at");
    private readonly string quotedFingerprint = dialect.QuoteIdentifier("fingerprint");

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
            $"SELECT {quotedMigrationId} AS MigrationId, {quotedAppliedOrder} AS AppliedOrder, {quotedAppliedAt} AS AppliedAt, {quotedFingerprint} AS Fingerprint FROM {quotedTable} ORDER BY {quotedAppliedOrder}",
            cancellationToken: cancellationToken);
        return rows.Select(row => new MigrationHistoryEntry(
            row.MigrationId,
            row.AppliedOrder,
            DateTimeOffset.Parse(row.AppliedAt, System.Globalization.CultureInfo.InvariantCulture),
            row.Fingerprint))
            .ToArray();
    }

    public async Task<long> GetNextAppliedOrderAsync(
        IDbExecutor executor,
        CancellationToken cancellationToken)
    {
        var current = await executor.ScalarAsync<long>(
            $"SELECT COALESCE(MAX({quotedAppliedOrder}), 0) FROM {quotedTable}",
            cancellationToken: cancellationToken);
        return checked(current + 1);
    }

    public Task InsertAsync(
        IDbExecutor executor,
        string migrationId,
        long appliedOrder,
        DateTimeOffset appliedAt,
        string fingerprint,
        CancellationToken cancellationToken) =>
        executor.ExecuteAsync(
            $"INSERT INTO {quotedTable} ({quotedMigrationId}, {quotedAppliedOrder}, {quotedAppliedAt}, {quotedFingerprint}) VALUES (@migrationId, @appliedOrder, @appliedAt, @fingerprint)",
            new
            {
                migrationId,
                appliedOrder,
                appliedAt = appliedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                fingerprint
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

        public long AppliedOrder { get; set; }

        public string AppliedAt { get; set; } = string.Empty;

        public string? Fingerprint { get; set; }
    }
}

public sealed record MigrationHistoryEntry(
    string MigrationId,
    long AppliedOrder,
    DateTimeOffset AppliedAt,
    string? Fingerprint);
