namespace SnapData.Migrations;

public sealed class MigrationPlan
{
    private readonly List<object> entries = [];

    public MigrationPlan()
    {
    }

    internal MigrationPlan(string providerName)
    {
        ProviderName = Required(providerName, nameof(providerName));
    }

    private MigrationPlan(MigrationOperation operation)
    {
        entries.Add(operation);
    }

    internal static MigrationPlan ForOperation(MigrationOperation operation) =>
        new(operation);

    public string? ProviderName { get; }

    public bool IsProvider(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        return string.Equals(
            ProviderName, providerName, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<MigrationOperation> Operations => entries
        .SelectMany(entry => entry switch
        {
            MigrationOperation operation => [operation],
            TableBuilder table => new MigrationOperation[] { table.Build() },
            AlterTableBuilder table => table.Build(),
            _ => throw new InvalidOperationException("Unsupported migration-plan entry.")
        })
        .ToArray();

    public TableBuilder CreateTable(string table)
    {
        var builder = new TableBuilder(table);
        entries.Add(builder);
        return builder;
    }

    public TableBuilder CreateTableIfNotExists(string table)
    {
        var builder = new TableBuilder(table, ifNotExists: true);
        entries.Add(builder);
        return builder;
    }

    public AlterTableBuilder AlterTable(string table)
    {
        var builder = new AlterTableBuilder(table);
        entries.Add(builder);
        return builder;
    }

    public void DropTable(string table) =>
        entries.Add(new DropTableOperation(Required(table, nameof(table))));

    public void DropTableIfExists(string table) =>
        entries.Add(new DropTableOperation(Required(table, nameof(table)))
        {
            Condition = MigrationOperationCondition.IfExists
        });

    public void RenameTable(string table, string newName) =>
        entries.Add(new RenameTableOperation(
            Required(table, nameof(table)),
            Required(newName, nameof(newName))));

    public void AddColumn(string table, ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);
        entries.Add(new AddColumnOperation(Required(table, nameof(table)), column));
    }

    public void AlterColumn(string table, ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);
        entries.Add(new AlterColumnOperation(Required(table, nameof(table)), column));
    }

    public void SetColumnDefault(string table, string column, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        entries.Add(new SetColumnDefaultOperation(
            Required(table, nameof(table)),
            Required(column, nameof(column)),
            value));
    }

    public void SetColumnDefaultSql(string table, string column, string sql) =>
        SetColumnDefault(table, column, new SqlDefault(sql));

    public void DropColumnDefault(string table, string column) =>
        entries.Add(new DropColumnDefaultOperation(
            Required(table, nameof(table)),
            Required(column, nameof(column))));

    public void DropColumn(string table, string column) =>
        entries.Add(new DropColumnOperation(
            Required(table, nameof(table)),
            Required(column, nameof(column))));

    public void RenameColumn(string table, string column, string newName) =>
        entries.Add(new RenameColumnOperation(
            Required(table, nameof(table)),
            Required(column, nameof(column)),
            Required(newName, nameof(newName))));

    public void CreateIndex(string table, IndexDefinition index)
    {
        ArgumentNullException.ThrowIfNull(index);
        entries.Add(new CreateIndexOperation(Required(table, nameof(table)), index));
    }

    public void DropIndex(string table, string index) =>
        entries.Add(new DropIndexOperation(
            Required(table, nameof(table)),
            Required(index, nameof(index))));

    public void AddForeignKey(string table, ForeignKeyDefinition foreignKey)
    {
        ArgumentNullException.ThrowIfNull(foreignKey);
        if (foreignKey.Name is null)
        {
            throw new ArgumentException(
                "A standalone foreign key must have a name.", nameof(foreignKey));
        }
        entries.Add(new AddForeignKeyOperation(
            Required(table, nameof(table)), foreignKey));
    }

    public void DropForeignKey(string table, string foreignKey) =>
        entries.Add(new DropForeignKeyOperation(
            Required(table, nameof(table)),
            Required(foreignKey, nameof(foreignKey))));

    public void AddCheck(string table, CheckConstraintDefinition check)
    {
        ArgumentNullException.ThrowIfNull(check);
        entries.Add(new AddCheckConstraintOperation(
            Required(table, nameof(table)), check));
    }

    public void DropCheck(string table, string check) =>
        entries.Add(new DropCheckConstraintOperation(
            Required(table, nameof(table)),
            Required(check, nameof(check))));

    public void ExecuteSql(string sql) =>
        entries.Add(new ExecuteSqlOperation(Required(sql, nameof(sql))));

    private static string Required(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameter)
            : value;
}
