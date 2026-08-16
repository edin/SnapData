namespace SnapData.Migrations;

public sealed class ColumnBuilder
{
    private readonly TableBuilder owner;
    private readonly string name;
    private readonly MigrationColumnType type;
    private bool nullable;
    private bool primaryKey;
    private bool unique;
    private bool identity;
    private object? defaultValue;
    private int? length;
    private int? precision;
    private int? scale;

    internal ColumnBuilder(TableBuilder owner, string name, MigrationColumnType type)
    {
        this.owner = owner;
        this.name = name;
        this.type = type;
    }

    public ColumnBuilder Nullable(bool value = true) => Mutate(() => nullable = value);
    public ColumnBuilder PrimaryKey(bool value = true) => Mutate(() => primaryKey = value);
    public ColumnBuilder Unique(bool value = true) => Mutate(() => unique = value);
    public ColumnBuilder Identity(bool value = true) => Mutate(() => identity = value);
    public ColumnBuilder Default(object? value) => Mutate(() => defaultValue = value);
    public ColumnBuilder DefaultSql(string sql) => Default(new SqlDefault(sql));

    public ColumnBuilder Length(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return Mutate(() => length = value);
    }

    public ColumnBuilder Precision(int value, int columnScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        ArgumentOutOfRangeException.ThrowIfNegative(columnScale);
        if (columnScale > value)
        {
            throw new ArgumentOutOfRangeException(nameof(columnScale));
        }

        return Mutate(() =>
        {
            precision = value;
            scale = columnScale;
        });
    }

    internal ColumnDefinition Build() => new(
        name, type, nullable, primaryKey, unique, identity,
        defaultValue, length, precision, scale);

    private ColumnBuilder Mutate(Action mutation)
    {
        owner.EnsureOpen();
        mutation();
        return this;
    }
}

public sealed record SqlDefault
{
    public SqlDefault(string sql)
    {
        Sql = string.IsNullOrWhiteSpace(sql)
            ? throw new ArgumentException("Default SQL is required.", nameof(sql))
            : sql;
    }

    public string Sql { get; }
}
