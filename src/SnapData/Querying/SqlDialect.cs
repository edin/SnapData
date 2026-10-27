using System.Text;

namespace SnapData;

public abstract class SqlDialect : IQueryCompiler
{
    public static SqlDialect Ansi { get; } = new AnsiSqlDialect();

    public virtual QueryCapabilities Capabilities { get; } = new()
    {
        MultiRowInsert = true,
        InsertReturning = true,
        UpdateReturning = true,
        DeleteReturning = true
    };

    public virtual GeneratedInsertPlan CompileGeneratedInsert(
        InsertQueryBuilder insert,
        IReadOnlyList<ColumnReference> generatedColumns)
    {
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(generatedColumns);
        if (generatedColumns.Count == 0)
        {
            throw new ArgumentException(
                "At least one generated column is required.",
                nameof(generatedColumns));
        }

        insert.Returning(
            generatedColumns[0],
            generatedColumns.Skip(1).ToArray());
        return new GeneratedInsertPlan(Compile(insert));
    }

    public SqlQuery Compile(ISqlQueryBuilder query) =>
        query switch
        {
            SelectQueryBuilder select => Compile(select),
            InsertQueryBuilder insert => Compile(insert),
            UpsertQueryBuilder upsert => Compile(upsert),
            UpdateQueryBuilder update => Compile(update),
            DeleteQueryBuilder delete => Compile(delete),
            _ => throw new NotSupportedException(
                $"{GetType().Name} cannot compile {query.GetType().Name}.")
        };

    public virtual GeneratedInsertPlan CompileGeneratedEntityInsert(
        InsertQueryBuilder insert, IReadOnlyList<PropertyMapping> generatedProperties)
    {
        ArgumentNullException.ThrowIfNull(generatedProperties);
        return CompileGeneratedInsert(insert, generatedProperties.Select(property => property.Column).ToArray());
    }

    public virtual SqlQuery Compile(UpsertQueryBuilder query) =>
        throw new NotSupportedException($"{GetType().Name} does not support explicit-target upsert.");

    protected SqlQuery CompileOnConflictUpsert(UpsertQueryBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var insert = Compile(query.Insert);
        var sql = new StringBuilder(insert.Text)
            .Append(" ON CONFLICT (")
            .Append(string.Join(", ", query.ConflictColumns.Select(column => QuoteIdentifier(column.Name))))
            .Append(") DO ");
        if (query.IgnoreConflict)
            sql.Append("NOTHING");
        else
            sql.Append("UPDATE SET ").Append(string.Join(", ", query.UpdatedColumns.Select(column =>
                $"{QuoteIdentifier(column.Name)} = excluded.{QuoteIdentifier(column.Name)}")));
        AppendReturning(sql, query.ReturningColumns);
        return new SqlQuery(sql.ToString(), insert.Parameters);
    }

    protected SqlQuery CompileSqlServerUpsert(UpsertQueryBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var row = query.Insert.RowsValue[0];
        var context = new CompilationContext(this);
        string Source(ColumnReference column) => $"[source].{QuoteIdentifier(column.Name)}";
        string Target(ColumnReference column) => $"[target].{QuoteIdentifier(column.Name)}";
        var sql = new StringBuilder("MERGE INTO ")
            .Append(QuoteTable(query.Insert.Table))
            .Append(" WITH (HOLDLOCK) AS [target] USING (VALUES (")
            .Append(string.Join(", ", row.Select(pair => context.CompileValue(pair.Value))))
            .Append(")) AS [source] (")
            .Append(string.Join(", ", row.Select(pair => QuoteIdentifier(pair.Key.Name))))
            .Append(") ON ")
            .Append(string.Join(" AND ", query.ConflictColumns.Select(column =>
                $"({Target(column)} = {Source(column)} OR ({Target(column)} IS NULL AND {Source(column)} IS NULL))")));
        if (!query.IgnoreConflict)
        {
            sql.Append(" WHEN MATCHED THEN UPDATE SET ")
                .Append(string.Join(", ", query.UpdatedColumns.Select(column => $"{Target(column)} = {Source(column)}")));
        }
        sql.Append(" WHEN NOT MATCHED THEN INSERT (")
            .Append(string.Join(", ", row.Select(pair => QuoteIdentifier(pair.Key.Name))))
            .Append(") VALUES (")
            .Append(string.Join(", ", row.Select(pair => Source(pair.Key))))
            .Append(')');
        AppendMutationOutput(sql, query.ReturningColumns, "INSERTED");
        sql.Append(';');
        return context.Query(sql);
    }

    public SqlQuery Compile(SelectQueryBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = new CompilationContext(this);
        return context.Query(CompileSelect(query, context));
    }

    private StringBuilder CompileSelect(
        SelectQueryBuilder query,
        CompilationContext context)
    {
        if (query.IsForUpdate)
        {
            if (!Capabilities.ForUpdate)
                throw new NotSupportedException($"{GetType().Name} does not support FOR UPDATE.");
            if (query.IsDistinct || query.Groups.Count > 0 || query.HavingPredicates.Count > 0 || query.Columns.Any(c => c is AggregateExpression))
                throw new NotSupportedException("FOR UPDATE requires a row-returning SELECT without DISTINCT, grouping or aggregates.");
        }
        var sql = new StringBuilder("SELECT ");
        AppendSelectModifiers(sql, query);

        sql.Append(string.Join(", ", query.Columns.Select(context.CompileSelection)))
            .Append(" FROM ")
            .Append(QuoteTable(query.Table));

        AppendJoins(sql, query.Joins, context);

        AppendWhere(sql, query.Predicates, context);
        if (query.Groups.Count > 0)
        {
            sql.Append(" GROUP BY ")
                .Append(string.Join(", ", query.Groups.Select(column => QuoteColumn(column, false))));
        }

        if (query.HavingPredicates.Count > 0)
        {
            sql.Append(" HAVING ")
                .Append(string.Join(" AND ", query.HavingPredicates.Select(context.CompilePredicate)));
        }

        if (query.Sorts.Count > 0)
        {
            sql.Append(" ORDER BY ");
            sql.Append(string.Join(", ", query.Sorts.Select(sort =>
                $"{QuoteColumn(sort.Column, false)}{(sort.Descending ? " DESC" : " ASC")}")));
        }

        AppendLimit(sql, query);
        if (query.IsForUpdate) sql.Append(" FOR UPDATE");
        return sql;
    }

    public SqlQuery Compile(InsertQueryBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.UsesDefaultValues)
        {
            var defaultSql = new StringBuilder("INSERT INTO ").Append(QuoteTable(query.Table));
            AppendMutationOutput(defaultSql, query.ReturningColumns, "INSERTED");
            defaultSql.Append(DefaultInsertClause);
            AppendReturning(defaultSql, query.ReturningColumns);
            return new SqlQuery(defaultSql.ToString());
        }
        var rows = query.RowsValue;
        if (rows.Count > 1 && (!Capabilities.MultiRowInsert || rows.Count > (Capabilities.MaxRowsPerInsert ?? int.MaxValue)))
            throw new NotSupportedException("The compiler does not support this number of rows in one INSERT.");
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("An INSERT requires at least one row.");
        }

        var columns = rows[0].Select(pair => pair.Key).ToArray();
        if (rows.Any(row => row.Count != columns.Length
            || row.Select(pair => pair.Key).Where((column, index) =>
                !InsertQueryBuilder.SameColumn(column, columns[index])).Any()))
        {
            throw new InvalidOperationException("Every inserted row must contain the same columns in the same order.");
        }

        var context = new CompilationContext(this);
        var sql = new StringBuilder("INSERT INTO ")
            .Append(QuoteTable(query.Table))
            .Append(" (")
            .Append(string.Join(", ", columns.Select(column => QuoteColumn(column, false))))
            .Append(')');

        AppendMutationOutput(sql, query.ReturningColumns, "INSERTED");
        sql.Append(" VALUES ")
            .Append(string.Join(", ", rows.Select(row =>
                $"({string.Join(", ", row.Select(pair => context.CompileValue(pair.Value)))})")));

        AppendReturning(sql, query.ReturningColumns);
        return context.Query(sql);
    }

    public SqlQuery Compile(UpdateQueryBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.EnsureSafe();
        if (query.Values.Count == 0)
        {
            throw new InvalidOperationException("An UPDATE requires at least one value.");
        }

        var context = new CompilationContext(this);
        var sql = new StringBuilder("UPDATE ")
            .Append(QuoteTable(query.Table))
            .Append(" SET ")
            .Append(string.Join(", ", query.Values.Select(pair =>
                $"{QuoteColumn(pair.Key, false)} = {context.CompileValue(pair.Value)}")));

        AppendMutationOutput(sql, query.ReturningColumns, "INSERTED");
        AppendWhere(sql, query.Predicate, context);
        AppendReturning(sql, query.ReturningColumns);
        return context.Query(sql);
    }

    public SqlQuery Compile(DeleteQueryBuilder query)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.EnsureSafe();
        var context = new CompilationContext(this);
        var sql = new StringBuilder("DELETE FROM ")
            .Append(QuoteTable(query.Table));

        AppendMutationOutput(sql, query.ReturningColumns, "DELETED");
        AppendWhere(sql, query.Predicate, context);
        AppendReturning(sql, query.ReturningColumns);
        return context.Query(sql);
    }

    protected internal abstract string QuoteIdentifier(string identifier);

    protected virtual string DefaultInsertClause => " DEFAULT VALUES";

    protected virtual void AppendSelectModifiers(
        StringBuilder sql,
        SelectQueryBuilder query)
    {
        if (query.IsDistinct)
        {
            sql.Append("DISTINCT ");
        }
    }

    protected virtual void AppendLimit(StringBuilder sql, SelectQueryBuilder query)
    {
        var limit = query.LimitValue;
        var offset = query.OffsetValue;
        if (limit is not null)
        {
            sql.Append(" LIMIT ").Append(limit.Value);
        }

        if (offset is not null)
        {
            sql.Append(" OFFSET ").Append(offset.Value);
        }
    }

    protected virtual void AppendReturning(
        StringBuilder sql,
        IReadOnlyList<ColumnReference> columns)
    {
        if (columns.Count > 0)
        {
            sql.Append(" RETURNING ")
                .Append(string.Join(", ", columns.Select(column => QuoteColumn(column, true))));
        }
    }

    protected virtual void AppendMutationOutput(
        StringBuilder sql,
        IReadOnlyList<ColumnReference> columns,
        string source)
    {
    }

    protected internal string QuoteTable(TableReference table)
    {
        var result = string.IsNullOrWhiteSpace(table.Schema)
            ? QuoteIdentifier(table.Name)
            : $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";
        return table.Alias is null
            ? result
            : $"{result} AS {QuoteIdentifier(table.Alias)}";
    }

    protected internal string QuoteColumn(ColumnReference column, bool includeAlias)
    {
        if (!includeAlias && column.Alias is not null)
        {
            throw new InvalidOperationException(
                $"Column alias '{column.Alias}' is not valid in this query position.");
        }

        var name = column.Name == "*" ? "*" : QuoteIdentifier(column.Name);
        var result = column.Qualifier is null
            ? name
            : $"{QuoteIdentifier(column.Qualifier)}.{name}";
        return column.Alias is null
            ? result
            : $"{result} AS {QuoteIdentifier(column.Alias)}";
    }

    private static void AppendWhere(
        StringBuilder sql,
        IReadOnlyList<PredicateExpression> predicates,
        CompilationContext context)
    {
        if (predicates.Count > 0)
        {
            sql.Append(" WHERE ")
                .Append(string.Join(" AND ", predicates.Select(context.CompilePredicate)));
        }
    }

    private static void AppendJoins(
        StringBuilder sql,
        IReadOnlyList<JoinClause> joins,
        CompilationContext context)
    {
        foreach (var join in joins)
        {
            sql.Append(' ').Append(join.Type switch
            {
                JoinType.Inner => "INNER JOIN ",
                JoinType.Left => "LEFT JOIN ",
                JoinType.Right => "RIGHT JOIN ",
                JoinType.Full => "FULL JOIN ",
                JoinType.Cross => "CROSS JOIN ",
                _ => throw new ArgumentOutOfRangeException(nameof(join.Type))
            });
            sql.Append(context.Dialect.QuoteTable(join.Table));
            if (join.Type != JoinType.Cross)
            {
                sql.Append(" ON ").Append(context.CompilePredicate(
                    join.Predicate ?? throw new InvalidOperationException(
                        $"{join.Type} join requires a predicate.")));
            }
        }
    }

    private static void AppendWhere(
        StringBuilder sql,
        PredicateExpression? predicate,
        CompilationContext context)
    {
        if (predicate is not null)
        {
            sql.Append(" WHERE ").Append(context.CompilePredicate(predicate));
        }
    }

    private sealed class AnsiSqlDialect : SqlDialect
    {
        protected internal override string QuoteIdentifier(string identifier) =>
            $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed class CompilationContext(SqlDialect dialect)
    {
        private int _parameterIndex;
        private Dictionary<string, object?> Parameters { get; } = new(StringComparer.Ordinal);

        internal SqlDialect Dialect => dialect;

        internal SqlQuery Query(StringBuilder sql) => new(sql.ToString(), Parameters);

        internal string CompileSelection(ISelectExpression expression) =>
            expression switch
            {
                ColumnReference column => dialect.QuoteColumn(column, true),
                AggregateExpression aggregate => CompileAggregate(aggregate, true),
                _ => throw new NotSupportedException(
                    $"Unsupported select expression {expression.GetType().Name}.")
            };

        internal string CompilePredicate(PredicateExpression expression) =>
            expression switch
            {
                ComparisonPredicate comparison =>
                    $"{CompileValue(comparison.Expression)} {Operator(comparison.Operator)} {CompileValue(comparison.Value)}",
                LogicalPredicate logical =>
                    $"({CompilePredicate(logical.Left)} {Logical(logical.Operator)} {CompilePredicate(logical.Right)})",
                NotPredicate not => $"NOT ({CompilePredicate(not.Operand)})",
                NullPredicate nullCheck =>
                    $"{dialect.QuoteColumn(nullCheck.Column.Reference, false)} IS {(nullCheck.Negated ? "NOT " : string.Empty)}NULL",
                InPredicate set => CompileIn(set),
                BetweenPredicate range =>
                    $"{dialect.QuoteColumn(range.Column.Reference, false)} BETWEEN {CompileValue(range.Minimum)} AND {CompileValue(range.Maximum)}",
                ExistsPredicate exists =>
                    $"{(exists.Negated ? "NOT " : string.Empty)}EXISTS ({CompileSubquery(exists.Query)})",
                InSubqueryPredicate set => CompileInSubquery(set),
                RawPredicate raw => raw.Sql,
                _ => throw new NotSupportedException($"Unsupported predicate {expression.GetType().Name}.")
            };

        internal string CompileValue(object? value) =>
            value switch
            {
                ColumnExpression column => dialect.QuoteColumn(column.Reference, false),
                AggregateExpression aggregate => CompileAggregate(aggregate, false),
                ParameterValueExpression parameter => Bind(parameter.Value),
                BinaryValueExpression binary =>
                    $"({CompileValue(binary.Left)} {Arithmetic(binary.Operator)} {CompileValue(binary.Right)})",
                RawValueExpression raw => raw.Sql,
                _ => Bind(value)
            };

        private string CompileAggregate(AggregateExpression aggregate, bool includeAlias)
        {
            var function = aggregate.Function switch
            {
                AggregateFunction.Count => "COUNT",
                AggregateFunction.Sum => "SUM",
                AggregateFunction.Average => "AVG",
                AggregateFunction.Minimum => "MIN",
                AggregateFunction.Maximum => "MAX",
                _ => throw new ArgumentOutOfRangeException(nameof(aggregate))
            };
            var value = dialect.QuoteColumn(aggregate.Column, false);
            var result = $"{function}({(aggregate.Distinct ? "DISTINCT " : string.Empty)}{value})";
            return includeAlias && aggregate.Alias is not null
                ? $"{result} AS {dialect.QuoteIdentifier(aggregate.Alias)}"
                : result;
        }

        private string CompileIn(InPredicate set)
        {
            if (set.Values.Count == 0)
            {
                return "1 = 0";
            }

            return $"{dialect.QuoteColumn(set.Column.Reference, false)} IN ({string.Join(", ", set.Values.Select(CompileValue))})";
        }

        private string CompileInSubquery(InSubqueryPredicate set)
        {
            if (set.Query.Columns.Count != 1)
            {
                throw new InvalidOperationException(
                    "An IN subquery must select exactly one expression.");
            }

            return $"{dialect.QuoteColumn(set.Column.Reference, false)} {(set.Negated ? "NOT " : string.Empty)}IN ({CompileSubquery(set.Query)})";
        }

        private string CompileSubquery(SelectQueryBuilder query) =>
            dialect.CompileSelect(query, this).ToString();

        private string Bind(object? value)
        {
            var name = $"p{++_parameterIndex}";
            Parameters.Add(name, value);
            return $"@{name}";
        }

        private static string Operator(ComparisonOperator value) =>
            value switch
            {
                ComparisonOperator.Equal => "=",
                ComparisonOperator.NotEqual => "<>",
                ComparisonOperator.GreaterThan => ">",
                ComparisonOperator.LessThan => "<",
                ComparisonOperator.GreaterThanOrEqual => ">=",
                ComparisonOperator.LessThanOrEqual => "<=",
                ComparisonOperator.Like => "LIKE",
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        private static string Logical(LogicalOperator value) =>
            value == LogicalOperator.And ? "AND" : "OR";

        private static string Arithmetic(ArithmeticOperator value) =>
            value switch
            {
                ArithmeticOperator.Add => "+",
                ArithmeticOperator.Subtract => "-",
                ArithmeticOperator.Multiply => "*",
                ArithmeticOperator.Divide => "/",
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };
    }
}
