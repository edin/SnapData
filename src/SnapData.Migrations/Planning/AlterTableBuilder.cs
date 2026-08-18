using SnapData.Schema;

namespace SnapData.Migrations;

public sealed class AlterTableBuilder : IDisposable, IColumnBuilderOwner
{
    private readonly string table;
    private readonly List<object> entries = [];
    private readonly HashSet<ColumnBuilder> changedColumns = [];
    private readonly Dictionary<ColumnBuilder, MigrationOperationCondition> columnConditions = [];
    private bool isSealed;

    internal AlterTableBuilder(string table)
    {
        this.table = RequiredName(table, nameof(table));
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

    public ConditionalAlterTableBuilder IfNotExists()
    {
        EnsureOpen();
        return new ConditionalAlterTableBuilder(
            this, MigrationOperationCondition.IfNotExists);
    }

    public ConditionalAlterTableBuilder IfExists()
    {
        EnsureOpen();
        return new ConditionalAlterTableBuilder(
            this, MigrationOperationCondition.IfExists);
    }

    public void DropColumn(string column)
    {
        EnsureOpen();
        entries.Add(new DropColumnOperation(table, RequiredName(column, nameof(column))));
    }

    public void RenameColumn(string column, string newName)
    {
        EnsureOpen();
        entries.Add(new RenameColumnOperation(
            table,
            RequiredName(column, nameof(column)),
            RequiredName(newName, nameof(newName))));
    }

    public void SetDefault(string column, object value)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(value);
        entries.Add(new SetColumnDefaultOperation(
            table, RequiredName(column, nameof(column)), value));
    }

    public void SetDefaultSql(string column, string sql) =>
        SetDefault(column, new SqlDefault(sql));

    public void DropDefault(string column)
    {
        EnsureOpen();
        entries.Add(new DropColumnDefaultOperation(
            table, RequiredName(column, nameof(column))));
    }

    public void CreateIndex(string? name, params IndexColumn[] columns)
    {
        EnsureOpen();
        entries.Add(new CreateIndexOperation(
            table, new IndexDefinition(name, columns)));
    }

    public void CreateUniqueIndex(string? name, params IndexColumn[] columns)
    {
        EnsureOpen();
        entries.Add(new CreateIndexOperation(
            table, new IndexDefinition(name, columns, isUnique: true)));
    }

    public void DropIndex(string name)
    {
        EnsureOpen();
        entries.Add(new DropIndexOperation(table, RequiredName(name, nameof(name))));
    }

    public void AddForeignKey(
        string name,
        IEnumerable<string> columns,
        string referencedTable,
        IEnumerable<string> referencedColumns,
        ReferentialAction onUpdate = ReferentialAction.NoAction,
        ReferentialAction onDelete = ReferentialAction.NoAction)
    {
        EnsureOpen();
        entries.Add(new AddForeignKeyOperation(
            table,
            new ForeignKeyDefinition(
                RequiredName(name, nameof(name)),
                columns,
                referencedTable,
                referencedColumns,
                onUpdate,
                onDelete)));
    }

    public void DropForeignKey(string name)
    {
        EnsureOpen();
        entries.Add(new DropForeignKeyOperation(
            table, RequiredName(name, nameof(name))));
    }

    public void AddCheck(string name, string predicate)
    {
        EnsureOpen();
        entries.Add(new AddCheckConstraintOperation(
            table, new CheckConstraintDefinition(name, predicate)));
    }

    public void DropCheck(string name)
    {
        EnsureOpen();
        entries.Add(new DropCheckConstraintOperation(
            table, RequiredName(name, nameof(name))));
    }

    public void Dispose() => isSealed = true;

    void IColumnBuilderOwner.EnsureOpen() => EnsureOpen();

    void IColumnBuilderOwner.MarkChanged(ColumnBuilder column)
    {
        EnsureOpen();
        if (!entries.Contains(column))
        {
            throw new InvalidOperationException(
                "The column does not belong to this AlterTable scope.");
        }
        if (columnConditions.TryGetValue(column, out var condition) &&
            condition != MigrationOperationCondition.None)
        {
            throw new InvalidOperationException(
                "Change() cannot be combined with IfExists() or IfNotExists().");
        }
        changedColumns.Add(column);
    }

    internal IReadOnlyList<MigrationOperation> Build()
    {
        if (!isSealed)
        {
            throw new InvalidOperationException(
                $"Table alteration '{table}' must be disposed before reading the plan.");
        }

        return entries.Select(entry => entry switch
        {
            ColumnBuilder column when changedColumns.Contains(column) =>
                new AlterColumnOperation(table, column.Build()),
            ColumnBuilder column => new AddColumnOperation(table, column.Build())
            {
                Condition = columnConditions.GetValueOrDefault(column)
            },
            MigrationOperation operation => operation,
            _ => throw new InvalidOperationException("Unsupported table-alteration entry.")
        }).ToArray();
    }

    private ColumnBuilder AddColumn(string name, MigrationColumnType type)
        => AddColumn(name, type, MigrationOperationCondition.None);

    internal ColumnBuilder AddColumn(
        string name,
        MigrationColumnType type,
        MigrationOperationCondition condition)
    {
        EnsureOpen();
        var column = new ColumnBuilder(this, RequiredName(name, nameof(name)), type);
        entries.Add(column);
        columnConditions.Add(column, condition);
        return column;
    }

    internal void AddConditionalDropColumn(
        string column,
        MigrationOperationCondition condition)
    {
        EnsureOpen();
        if (condition != MigrationOperationCondition.IfExists)
        {
            throw new InvalidOperationException(
                "DropColumn can only be combined with IfExists().");
        }
        entries.Add(new DropColumnOperation(table, RequiredName(column, nameof(column)))
        {
            Condition = condition
        });
    }

    internal void AddConditionalIndex(
        string? name,
        IEnumerable<IndexColumn> columns,
        bool isUnique,
        MigrationOperationCondition condition)
    {
        EnsureOpen();
        if (condition != MigrationOperationCondition.IfNotExists)
        {
            throw new InvalidOperationException(
                "CreateIndex can only be combined with IfNotExists().");
        }
        entries.Add(new CreateIndexOperation(
            table, new IndexDefinition(name, columns, isUnique))
        {
            Condition = condition
        });
    }

    internal void AddConditionalDropIndex(
        string name,
        MigrationOperationCondition condition)
    {
        EnsureOpen();
        if (condition != MigrationOperationCondition.IfExists)
        {
            throw new InvalidOperationException(
                "DropIndex can only be combined with IfExists().");
        }
        entries.Add(new DropIndexOperation(table, RequiredName(name, nameof(name)))
        {
            Condition = condition
        });
    }

    private void EnsureOpen()
    {
        if (isSealed)
        {
            throw new InvalidOperationException($"Table alteration '{table}' is sealed.");
        }
    }

    private static string RequiredName(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A name is required.", parameter)
            : value;
}

public sealed class ConditionalAlterTableBuilder
{
    private readonly AlterTableBuilder owner;
    private readonly MigrationOperationCondition condition;
    private bool consumed;

    internal ConditionalAlterTableBuilder(
        AlterTableBuilder owner,
        MigrationOperationCondition condition)
    {
        this.owner = owner;
        this.condition = condition;
    }

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

    public void DropColumn(string column) =>
        Consume(() => owner.AddConditionalDropColumn(column, condition));

    public void CreateIndex(string? name, params IndexColumn[] columns) =>
        Consume(() => owner.AddConditionalIndex(
            name, columns, isUnique: false, condition));

    public void CreateUniqueIndex(string? name, params IndexColumn[] columns) =>
        Consume(() => owner.AddConditionalIndex(
            name, columns, isUnique: true, condition));

    public void DropIndex(string name) =>
        Consume(() => owner.AddConditionalDropIndex(name, condition));

    private ColumnBuilder AddColumn(string name, MigrationColumnType type)
    {
        if (condition != MigrationOperationCondition.IfNotExists)
        {
            throw new InvalidOperationException(
                "Adding a column can only be combined with IfNotExists().");
        }
        ColumnBuilder? column = null;
        Consume(() => column = owner.AddColumn(name, type, condition));
        return column!;
    }

    private void Consume(Action operation)
    {
        if (consumed)
        {
            throw new InvalidOperationException(
                "IfExists() and IfNotExists() apply to exactly one operation.");
        }
        operation();
        consumed = true;
    }
}
