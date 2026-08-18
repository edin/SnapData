namespace SnapData.Migrations;

public abstract record MigrationOperation
{
    public MigrationOperationCondition Condition { get; init; }
}

public enum MigrationOperationCondition
{
    None,
    IfExists,
    IfNotExists
}

public sealed record CreateTableOperation : MigrationOperation
{
    public CreateTableOperation(
        string table,
        IEnumerable<ColumnDefinition> columns,
        IEnumerable<IndexDefinition>? indexes = null,
        IEnumerable<ForeignKeyDefinition>? foreignKeys = null,
        bool ifNotExists = false)
    {
        Table = RequiredName(table, nameof(table));
        Columns = Array.AsReadOnly(columns?.ToArray()
            ?? throw new ArgumentNullException(nameof(columns)));
        Indexes = Array.AsReadOnly(indexes?.ToArray() ?? []);
        ForeignKeys = Array.AsReadOnly(foreignKeys?.ToArray() ?? []);
        IfNotExists = ifNotExists;
        EnsureUnique(Columns.Select(column => column.Name), "column");
        EnsureUnique(
            Indexes.Where(index => index.Name is not null).Select(index => index.Name!),
            "index");
        EnsureUnique(
            ForeignKeys.Where(key => key.Name is not null).Select(key => key.Name!),
            "foreign key");

        var columnNames = Columns.Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownIndexColumn = Indexes.SelectMany(index => index.Columns)
            .FirstOrDefault(column => !columnNames.Contains(column.Name));
        if (unknownIndexColumn is not null)
        {
            throw new InvalidOperationException(
                $"Index column '{unknownIndexColumn.Name}' is not defined on table '{Table}'.");
        }
        var unknownForeignKeyColumn = ForeignKeys.SelectMany(key => key.Columns)
            .FirstOrDefault(column => !columnNames.Contains(column));
        if (unknownForeignKeyColumn is not null)
        {
            throw new InvalidOperationException(
                $"Foreign-key column '{unknownForeignKeyColumn}' is not defined on table '{Table}'.");
        }
    }

    public string Table { get; }
    public IReadOnlyList<ColumnDefinition> Columns { get; }
    public IReadOnlyList<IndexDefinition> Indexes { get; }
    public IReadOnlyList<ForeignKeyDefinition> ForeignKeys { get; }
    public bool IfNotExists { get; }

    private static string RequiredName(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A table name is required.", parameter)
            : value;

    private static void EnsureUnique(IEnumerable<string> names, string kind)
    {
        var duplicate = names
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate {kind} name '{duplicate.Key}'.");
        }
    }
}

public sealed record DropTableOperation(string Table) : MigrationOperation;

public sealed record RenameTableOperation(
    string Table,
    string NewName) : MigrationOperation;

public sealed record AddColumnOperation(
    string Table,
    ColumnDefinition Column) : MigrationOperation;

public sealed record AlterColumnOperation(
    string Table,
    ColumnDefinition Column) : MigrationOperation;

public sealed record SetColumnDefaultOperation(
    string Table,
    string Column,
    object Value) : MigrationOperation;

public sealed record DropColumnDefaultOperation(
    string Table,
    string Column) : MigrationOperation;

public sealed record DropColumnOperation(string Table, string Column) : MigrationOperation;

public sealed record RenameColumnOperation(
    string Table,
    string Column,
    string NewName) : MigrationOperation;

public sealed record CreateIndexOperation(
    string Table,
    IndexDefinition Index) : MigrationOperation;

public sealed record DropIndexOperation(
    string Table,
    string Index) : MigrationOperation;

public sealed record AddForeignKeyOperation(
    string Table,
    ForeignKeyDefinition ForeignKey) : MigrationOperation;

public sealed record DropForeignKeyOperation(
    string Table,
    string ForeignKey) : MigrationOperation;

public sealed record ExecuteSqlOperation(string Sql) : MigrationOperation;
