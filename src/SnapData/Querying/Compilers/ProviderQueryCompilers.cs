using System.Text;

namespace SnapData;

public sealed class SqliteQueryCompiler : SqlDialect
{
    public static SqliteQueryCompiler Instance { get; } = new();

    private SqliteQueryCompiler()
    {
    }

    protected internal override string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

public sealed class PostgresQueryCompiler : SqlDialect
{
    public static PostgresQueryCompiler Instance { get; } = new();

    private PostgresQueryCompiler()
    {
    }

    protected internal override string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

public sealed class FirebirdQueryCompiler : SqlDialect
{
    public static FirebirdQueryCompiler Instance { get; } = new();

    private FirebirdQueryCompiler()
    {
    }

    protected internal override string QuoteIdentifier(string identifier) =>
        $"\"{identifier.ToUpperInvariant().Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    protected override void AppendLimit(StringBuilder sql, SelectQueryBuilder query)
    {
        if (query.OffsetValue is { } offset)
        {
            sql.Append(" OFFSET ").Append(offset).Append(" ROWS");
        }

        if (query.LimitValue is { } limit)
        {
            sql.Append(query.OffsetValue is null ? " FETCH FIRST " : " FETCH NEXT ")
                .Append(limit)
                .Append(" ROWS ONLY");
        }
    }
}

public sealed class MySqlQueryCompiler : SqlDialect
{
    public static MySqlQueryCompiler Instance { get; } = new();

    private MySqlQueryCompiler()
    {
    }

    public override GeneratedInsertPlan CompileGeneratedInsert(
        InsertQueryBuilder insert,
        IReadOnlyList<ColumnReference> generatedColumns)
    {
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(generatedColumns);
        if (generatedColumns.Count != 1)
        {
            throw new NotSupportedException(
                "MySQL generated inserts require exactly one generated column.");
        }

        return new GeneratedInsertPlan(
            Compile(insert),
            new SqlQuery("SELECT LAST_INSERT_ID()"));
    }

    protected internal override string QuoteIdentifier(string identifier) =>
        $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    protected override void AppendLimit(StringBuilder sql, SelectQueryBuilder query)
    {
        var limit = query.LimitValue;
        var offset = query.OffsetValue;
        if (limit is not null)
        {
            sql.Append(" LIMIT ").Append(limit.Value);
            if (offset is not null)
            {
                sql.Append(" OFFSET ").Append(offset.Value);
            }

            return;
        }

        if (offset is not null)
        {
            throw new NotSupportedException("MySQL OFFSET requires LIMIT.");
        }
    }

    protected override void AppendReturning(
        StringBuilder sql,
        IReadOnlyList<ColumnReference> columns)
    {
        if (columns.Count > 0)
        {
            throw new NotSupportedException("MySQL does not support RETURNING for these mutations.");
        }
    }
}

public sealed class SqlServerQueryCompiler : SqlDialect
{
    public static SqlServerQueryCompiler Instance { get; } = new();

    private SqlServerQueryCompiler()
    {
    }

    protected internal override string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    protected override void AppendSelectModifiers(
        StringBuilder sql,
        SelectQueryBuilder query)
    {
        base.AppendSelectModifiers(sql, query);
        if (query.LimitValue is { } limit && query.OffsetValue is null)
        {
            sql.Append("TOP (").Append(limit).Append(") ");
        }
    }

    protected override void AppendLimit(StringBuilder sql, SelectQueryBuilder query)
    {
        if (query.OffsetValue is not { } offset)
        {
            return;
        }

        if (query.Sorts.Count == 0)
        {
            throw new InvalidOperationException(
                "SQL Server OFFSET requires at least one OrderBy().");
        }

        sql.Append(" OFFSET ").Append(offset).Append(" ROWS");
        if (query.LimitValue is { } limit)
        {
            sql.Append(" FETCH NEXT ").Append(limit).Append(" ROWS ONLY");
        }
    }

    protected override void AppendMutationOutput(
        StringBuilder sql,
        IReadOnlyList<ColumnReference> columns,
        string source)
    {
        if (columns.Count == 0)
        {
            return;
        }

        sql.Append(" OUTPUT ")
            .Append(string.Join(", ", columns.Select(column =>
            {
                var result = $"{source}.{QuoteIdentifier(column.Name)}";
                return column.Alias is null
                    ? result
                    : $"{result} AS {QuoteIdentifier(column.Alias)}";
            })));
    }

    protected override void AppendReturning(
        StringBuilder sql,
        IReadOnlyList<ColumnReference> columns)
    {
    }
}
