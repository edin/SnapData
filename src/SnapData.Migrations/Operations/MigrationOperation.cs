namespace SnapData.Migrations;

public abstract record MigrationOperation;

public sealed record CreateTableOperation : MigrationOperation
{
    public CreateTableOperation(
        string table,
        IEnumerable<ColumnDefinition> columns,
        IEnumerable<IndexDefinition>? indexes = null,
        IEnumerable<ForeignKeyDefinition>? foreignKeys = null)
    {
        Table = RequiredName(table, nameof(table));
        Columns = Array.AsReadOnly(columns?.ToArray()
            ?? throw new ArgumentNullException(nameof(columns)));
        Indexes = Array.AsReadOnly(indexes?.ToArray() ?? []);
        ForeignKeys = Array.AsReadOnly(foreignKeys?.ToArray() ?? []);
    }

    public string Table { get; }
    public IReadOnlyList<ColumnDefinition> Columns { get; }
    public IReadOnlyList<IndexDefinition> Indexes { get; }
    public IReadOnlyList<ForeignKeyDefinition> ForeignKeys { get; }

    private static string RequiredName(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A table name is required.", parameter)
            : value;
}

public sealed record DropTableOperation(string Table) : MigrationOperation;

public sealed record DropColumnOperation(string Table, string Column) : MigrationOperation;

public sealed record RenameColumnOperation(
    string Table,
    string Column,
    string NewName) : MigrationOperation;

public sealed record ExecuteSqlOperation(string Sql) : MigrationOperation;
