using SnapData.Schema;

namespace SnapData.Migrations;

public sealed class TableBuilder : IDisposable, IColumnBuilderOwner
{
    private readonly string table;
    private readonly bool ifNotExists;
    private readonly List<ColumnBuilder> columns = [];
    private readonly List<IndexDefinition> indexes = [];
    private readonly List<ForeignKeyDefinition> foreignKeys = [];
    private readonly List<CheckConstraintDefinition> checks = [];
    private bool isSealed;

    internal TableBuilder(string table, bool ifNotExists = false)
    {
        this.table = RequiredName(table, nameof(table));
        this.ifNotExists = ifNotExists;
    }

    public bool IsSealed => isSealed;

    public ColumnBuilder Identity(string name = "id") =>
        AddColumn(name, MigrationColumnType.Int64).PrimaryKey().Identity();

    public ColumnBuilder Int16(string name) => AddColumn(name, MigrationColumnType.Int16);
    public ColumnBuilder Int32(string name) => AddColumn(name, MigrationColumnType.Int32);
    public ColumnBuilder Int64(string name) => AddColumn(name, MigrationColumnType.Int64);
    public ColumnBuilder String(string name, int length = 255) =>
        AddColumn(name, MigrationColumnType.String).Length(length);
    public ColumnBuilder Text(string name) => AddColumn(name, MigrationColumnType.Text);
    public ColumnBuilder Boolean(string name) => AddColumn(name, MigrationColumnType.Boolean);
    public ColumnBuilder Decimal(string name, int precision = 18, int scale = 2) =>
        AddColumn(name, MigrationColumnType.Decimal).Precision(precision, scale);
    public ColumnBuilder Float(string name) => AddColumn(name, MigrationColumnType.Float);
    public ColumnBuilder Double(string name) => AddColumn(name, MigrationColumnType.Double);
    public ColumnBuilder Guid(string name) => AddColumn(name, MigrationColumnType.Guid);
    public ColumnBuilder Binary(string name) => AddColumn(name, MigrationColumnType.Binary);
    public ColumnBuilder Date(string name) => AddColumn(name, MigrationColumnType.Date);
    public ColumnBuilder Time(string name) => AddColumn(name, MigrationColumnType.Time);
    public ColumnBuilder DateTime(string name) => AddColumn(name, MigrationColumnType.DateTime);
    public ColumnBuilder DateTimeOffset(string name) =>
        AddColumn(name, MigrationColumnType.DateTimeOffset);
    public ColumnBuilder Json(string name) => AddColumn(name, MigrationColumnType.Json);

    public void Timestamps()
    {
        DateTime("created_at").DefaultSql("CURRENT_TIMESTAMP");
        DateTime("updated_at").Nullable();
    }

    public void Index(string? name, params IndexColumn[] columns)
    {
        EnsureOpen();
        indexes.Add(new IndexDefinition(name, columns));
    }

    public void Unique(string? name, params IndexColumn[] columns)
    {
        EnsureOpen();
        indexes.Add(new IndexDefinition(name, columns, isUnique: true));
    }

    public void ForeignKey(
        string? name,
        IEnumerable<string> columns,
        string referencedTable,
        IEnumerable<string> referencedColumns,
        ReferentialAction onUpdate = ReferentialAction.NoAction,
        ReferentialAction onDelete = ReferentialAction.NoAction)
    {
        EnsureOpen();
        foreignKeys.Add(new ForeignKeyDefinition(
            name, columns, referencedTable, referencedColumns, onUpdate, onDelete));
    }

    public void Check(string name, string predicate)
    {
        EnsureOpen();
        checks.Add(new CheckConstraintDefinition(name, predicate));
    }

    public void Dispose() => isSealed = true;

    internal void EnsureOpen()
    {
        if (isSealed)
        {
            throw new InvalidOperationException($"Table definition '{table}' is sealed.");
        }
    }

    void IColumnBuilderOwner.EnsureOpen() => EnsureOpen();

    void IColumnBuilderOwner.MarkChanged(ColumnBuilder column) =>
        throw new InvalidOperationException(
            "Change() can only be used inside an AlterTable scope.");

    internal CreateTableOperation Build()
    {
        if (!isSealed)
        {
            throw new InvalidOperationException(
                $"Table definition '{table}' must be disposed before reading the plan.");
        }

        return new CreateTableOperation(
            table,
            columns.Select(column => column.Build()),
            indexes,
            foreignKeys,
            ifNotExists,
            checks);
    }

    private ColumnBuilder AddColumn(string name, MigrationColumnType type)
    {
        EnsureOpen();
        var column = new ColumnBuilder(this, RequiredName(name, nameof(name)), type);
        columns.Add(column);
        return column;
    }

    private static string RequiredName(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A name is required.", parameter)
            : value;
}
