using SnapData.Schema;

namespace SnapData.Migrations;

public sealed class MigrationSchema
{
    private readonly ISchemaInspector inspector;
    private readonly CancellationToken cancellationToken;

    internal bool WasAccessed { get; private set; }

    internal MigrationSchema(
        ISchemaInspector inspector,
        CancellationToken cancellationToken)
    {
        this.inspector = inspector;
        this.cancellationToken = cancellationToken;
    }

    public Task<bool> TableExistsAsync(string table)
    {
        WasAccessed = true;
        return inspector.TableExistsAsync(ParseName(table), cancellationToken);
    }

    public Task<bool> ColumnExistsAsync(string table, string column)
    {
        WasAccessed = true;
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return inspector.ColumnExistsAsync(
            ParseName(table),
            column,
            cancellationToken);
    }

    public Task<TableSchema?> GetTableAsync(
        string table,
        SchemaReadOptions? options = null)
    {
        WasAccessed = true;
        return inspector.GetTableAsync(ParseName(table), options, cancellationToken);
    }

    public Task<DatabaseSchema> ReadAsync(SchemaReadOptions? options = null)
    {
        WasAccessed = true;
        return inspector.ReadAsync(options, cancellationToken);
    }

    internal static SchemaObjectName ParseName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 when parts[0].Length > 0 => new SchemaObjectName(parts[0]),
            2 when parts[0].Length > 0 && parts[1].Length > 0 =>
                new SchemaObjectName(parts[1], parts[0]),
            _ => throw new ArgumentException(
                "A table name must use 'table' or 'schema.table' form.",
                nameof(value))
        };
    }
}
