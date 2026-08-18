using System.Globalization;
using System.Text;
using SnapData.Schema;

namespace SnapData.Migrations;

public abstract class RelationalMigrationCompiler : IMigrationCompiler
{
    protected abstract string ProviderName { get; }

    public MigrationScript Compile(string migrationId, MigrationDirection direction, MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new MigrationScript(
            migrationId,
            direction,
            plan.Operations.SelectMany(CompileOperation).ToArray());
    }

    protected abstract string QuoteIdentifier(string identifier);

    protected abstract string StoreType(ColumnDefinition column);

    protected abstract string IdentityClause { get; }

    protected abstract string BooleanLiteral(bool value);

    protected virtual string ReferentialActionSql(ReferentialAction action) => action switch
    {
        ReferentialAction.Restrict => "RESTRICT",
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.SetDefault => "SET DEFAULT",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    protected virtual string BinaryLiteral(byte[] value) =>
        throw new NotSupportedException(
            $"{ProviderName} binary defaults require an explicit SqlDefault expression.");

    protected virtual string CompileDropColumn(DropColumnOperation operation) =>
        $"ALTER TABLE {QuoteTable(operation.Table)} DROP COLUMN {QuoteIdentifier(operation.Column)}";

    protected virtual string CompileAddColumn(AddColumnOperation operation) =>
        $"ALTER TABLE {QuoteTable(operation.Table)} ADD {CompileColumn(operation.Column)}";

    protected virtual IEnumerable<string> CompileAlterColumn(AlterColumnOperation operation) =>
        throw new NotSupportedException(
            $"{ProviderName} does not support altering columns.");

    protected virtual IEnumerable<string> CompileSetColumnDefault(
        SetColumnDefaultOperation operation)
    {
        yield return $"ALTER TABLE {QuoteTable(operation.Table)} ALTER COLUMN " +
            $"{QuoteIdentifier(operation.Column)} SET DEFAULT {FormatDefault(operation.Value)}";
    }

    protected virtual IEnumerable<string> CompileDropColumnDefault(
        DropColumnDefaultOperation operation)
    {
        yield return $"ALTER TABLE {QuoteTable(operation.Table)} ALTER COLUMN " +
            $"{QuoteIdentifier(operation.Column)} DROP DEFAULT";
    }

    protected virtual string CompileRenameColumn(RenameColumnOperation operation) =>
        $"ALTER TABLE {QuoteTable(operation.Table)} RENAME COLUMN {QuoteIdentifier(operation.Column)} TO {QuoteIdentifier(operation.NewName)}";

    protected virtual string CompileRenameTable(RenameTableOperation operation) =>
        $"ALTER TABLE {QuoteTable(operation.Table)} RENAME TO {QuoteIdentifier(RenameTarget(operation.NewName))}";

    protected virtual string CompileIndex(string table, IndexDefinition index)
    {
        var name = index.Name ?? DefaultIndexName(table, index);
        var columns = string.Join(", ", index.Columns.Select(column =>
            $"{QuoteIdentifier(column.Name)} {(column.Order == MigrationSortOrder.Descending ? "DESC" : "ASC")}"));
        return $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX {QuoteIdentifier(name)} ON {QuoteTable(table)} ({columns})";
    }

    protected virtual string CompileDropIndex(DropIndexOperation operation) =>
        $"DROP INDEX {QuoteIdentifier(operation.Index)}";

    protected virtual string CompileAddForeignKey(AddForeignKeyOperation operation)
    {
        if (operation.ForeignKey.Name is null)
        {
            throw new InvalidOperationException(
                "A standalone foreign key must have a name.");
        }
        return $"ALTER TABLE {QuoteTable(operation.Table)} ADD {CompileForeignKey(operation.ForeignKey)}";
    }

    protected virtual string CompileDropForeignKey(DropForeignKeyOperation operation) =>
        $"ALTER TABLE {QuoteTable(operation.Table)} DROP CONSTRAINT {QuoteIdentifier(operation.ForeignKey)}";

    protected virtual IEnumerable<string> CompileCreateTableIfNotExists(
        CreateTableOperation operation,
        string createTableSql,
        IReadOnlyList<string> indexSql) =>
        throw new NotSupportedException(
            $"{ProviderName} CreateTableIfNotExists compilation has not been implemented.");

    protected virtual string QuoteTable(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        var parts = table.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A table name must use 'table' or 'schema.table' form.", nameof(table));
        }
        return string.Join(".", parts.Select(QuoteIdentifier));
    }

    private IEnumerable<MigrationStatement> CompileOperation(MigrationOperation operation)
    {
        var statements = operation switch
        {
            CreateTableOperation createTable => CompileCreateTable(createTable),
            DropTableOperation dropTable => Single($"DROP TABLE {QuoteTable(dropTable.Table)}"),
            RenameTableOperation renameTable => Single(CompileRenameTable(renameTable)),
            AddColumnOperation addColumn => Single(CompileAddColumn(addColumn)),
            AlterColumnOperation alterColumn => CompileAlterColumn(alterColumn)
                .Select(sql => new MigrationStatement(sql)),
            SetColumnDefaultOperation setDefault => CompileSetColumnDefault(setDefault)
                .Select(sql => new MigrationStatement(sql)),
            DropColumnDefaultOperation dropDefault => CompileDropColumnDefault(dropDefault)
                .Select(sql => new MigrationStatement(sql)),
            DropColumnOperation dropColumn => Single(CompileDropColumn(dropColumn)),
            RenameColumnOperation renameColumn => Single(CompileRenameColumn(renameColumn)),
            CreateIndexOperation createIndex => Single(
                CompileIndex(createIndex.Table, createIndex.Index)),
            DropIndexOperation dropIndex => Single(CompileDropIndex(dropIndex)),
            AddForeignKeyOperation addForeignKey => Single(
                CompileAddForeignKey(addForeignKey)),
            DropForeignKeyOperation dropForeignKey => Single(
                CompileDropForeignKey(dropForeignKey)),
            ExecuteSqlOperation executeSql => Single(executeSql.Sql),
            _ => throw new NotSupportedException(
                $"{ProviderName} does not support migration operation '{operation.GetType().Name}'.")
        };
        return statements.Select(statement =>
            MigrationOperationConditions.Apply(operation, statement));
    }

    private IEnumerable<MigrationStatement> CompileCreateTable(CreateTableOperation operation)
    {
        if (operation.Columns.Count == 0)
        {
            throw new InvalidOperationException(
                $"Table '{operation.Table}' must define at least one column.");
        }

        var primaryKeys = operation.Columns.Where(column => column.IsPrimaryKey).ToArray();
        var identities = operation.Columns.Where(column => column.IsIdentity).ToArray();
        if (identities.Length > 1 || identities.Any(column =>
            !column.IsPrimaryKey || column.IsNullable || primaryKeys.Length != 1 ||
            column.Type is not (MigrationColumnType.Int16 or MigrationColumnType.Int32 or MigrationColumnType.Int64)))
        {
            throw new InvalidOperationException(
                $"{ProviderName} requires one identity column to be the single non-nullable primary key.");
        }

        var definitions = operation.Columns.Select(CompileColumn).ToList();
        if (primaryKeys.Length > 0)
        {
            definitions.Add(
                $"PRIMARY KEY ({string.Join(", ", primaryKeys.Select(column => QuoteIdentifier(column.Name)))})");
        }
        definitions.AddRange(operation.ForeignKeys.Select(CompileForeignKey));

        var sql = new StringBuilder()
            .Append("CREATE TABLE ").Append(QuoteTable(operation.Table)).AppendLine(" (")
            .Append("    ")
            .Append(string.Join("," + Environment.NewLine + "    ", definitions))
            .AppendLine().Append(')')
            .ToString();
        var createTableSql = sql.ToString();
        var indexSql = operation.Indexes
            .Select(index => CompileIndex(operation.Table, index))
            .ToArray();
        var statements = operation.IfNotExists
            ? CompileCreateTableIfNotExists(operation, createTableSql, indexSql)
            : new[] { createTableSql }.Concat(indexSql);
        foreach (var statement in statements)
        {
            yield return new MigrationStatement(statement);
        }
    }

    protected virtual string CompileColumn(ColumnDefinition column)
    {
        var sql = new StringBuilder()
            .Append(QuoteIdentifier(column.Name)).Append(' ').Append(StoreType(column));
        if (column.IsIdentity)
        {
            sql.Append(' ').Append(IdentityClause);
        }
        if (!column.IsNullable)
        {
            sql.Append(" NOT NULL");
        }
        if (column.IsUnique)
        {
            sql.Append(" UNIQUE");
        }
        if (column.DefaultValue is not null)
        {
            sql.Append(" DEFAULT ").Append(FormatDefault(column.DefaultValue));
        }
        return sql.ToString();
    }

    protected string CompileForeignKey(ForeignKeyDefinition foreignKey)
    {
        var sql = new StringBuilder();
        if (foreignKey.Name is not null)
        {
            sql.Append("CONSTRAINT ").Append(QuoteIdentifier(foreignKey.Name)).Append(' ');
        }
        sql.Append("FOREIGN KEY (")
            .Append(string.Join(", ", foreignKey.Columns.Select(QuoteIdentifier)))
            .Append(") REFERENCES ").Append(QuoteTable(foreignKey.ReferencedTable))
            .Append(" (")
            .Append(string.Join(", ", foreignKey.ReferencedColumns.Select(QuoteIdentifier)))
            .Append(')');
        AppendAction(sql, "UPDATE", foreignKey.OnUpdate);
        AppendAction(sql, "DELETE", foreignKey.OnDelete);
        return sql.ToString();
    }

    private void AppendAction(StringBuilder sql, string operation, ReferentialAction action)
    {
        if (action == ReferentialAction.NoAction)
        {
            return;
        }
        sql.Append(" ON ").Append(operation).Append(' ').Append(ReferentialActionSql(action));
    }

    protected string FormatDefault(object value) => value switch
    {
        SqlDefault sql => sql.Sql,
        string text => QuoteLiteral(text),
        char character => QuoteLiteral(character.ToString()),
        bool boolean => BooleanLiteral(boolean),
        Guid guid => QuoteLiteral(guid.ToString()),
        DateTime dateTime => QuoteLiteral(dateTime.ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset offset => QuoteLiteral(offset.ToString("O", CultureInfo.InvariantCulture)),
        DateOnly date => QuoteLiteral(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        TimeOnly time => QuoteLiteral(time.ToString("O", CultureInfo.InvariantCulture)),
        byte[] bytes => BinaryLiteral(bytes),
        Enum enumeration => Convert.ToInt64(enumeration, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
            ?? throw UnsupportedDefault(value),
        _ => throw UnsupportedDefault(value)
    };

    private NotSupportedException UnsupportedDefault(object value) => new(
        $"{ProviderName} cannot format a default value of type '{value.GetType().Name}'. Use SqlDefault for SQL expressions.");

    protected static string QuoteLiteral(string value) => $"'{value.Replace("'", "''")}'";

    protected static string DefaultIndexName(string table, IndexDefinition index)
        => MigrationIndexName.Get(table, index);

    protected static string RenameTarget(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A renamed table's new name must be unqualified. Moving a table between schemas is provider-specific.",
                nameof(newName));
        }
        return newName;
    }

    protected void EnsureAlterColumnHasNoConstraintChanges(ColumnDefinition column)
    {
        if (column.IsPrimaryKey || column.IsUnique || column.IsIdentity)
        {
            throw new NotSupportedException(
                $"{ProviderName} AlterColumn does not change primary-key, unique, or identity constraints. " +
                "Use the corresponding constraint operation or raw SQL.");
        }
        if (column.DefaultValue is not null)
        {
            throw new NotSupportedException(
                $"{ProviderName} AlterColumn does not change column defaults. " +
                "Use SetDefault or DropDefault as a separate operation.");
        }
    }

    private static IEnumerable<MigrationStatement> Single(string sql)
    {
        yield return new MigrationStatement(sql);
    }
}
