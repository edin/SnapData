namespace SnapData.Migrations;

public sealed class MigrationPlan
{
    private readonly List<object> entries = [];

    public IReadOnlyList<MigrationOperation> Operations => entries
        .Select(entry => entry switch
        {
            MigrationOperation operation => operation,
            TableBuilder table => table.Build(),
            _ => throw new InvalidOperationException("Unsupported migration-plan entry.")
        })
        .ToArray();

    public TableBuilder CreateTable(string table)
    {
        var builder = new TableBuilder(table);
        entries.Add(builder);
        return builder;
    }

    public void DropTable(string table) =>
        entries.Add(new DropTableOperation(Required(table, nameof(table))));

    public void DropColumn(string table, string column) =>
        entries.Add(new DropColumnOperation(
            Required(table, nameof(table)),
            Required(column, nameof(column))));

    public void RenameColumn(string table, string column, string newName) =>
        entries.Add(new RenameColumnOperation(
            Required(table, nameof(table)),
            Required(column, nameof(column)),
            Required(newName, nameof(newName))));

    public void ExecuteSql(string sql) =>
        entries.Add(new ExecuteSqlOperation(Required(sql, nameof(sql))));

    private static string Required(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameter)
            : value;
}
