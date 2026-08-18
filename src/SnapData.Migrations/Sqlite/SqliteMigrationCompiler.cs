using System.Globalization;
using System.Text;
using SnapData.Schema;

namespace SnapData.Migrations;

public sealed class SqliteMigrationCompiler : IMigrationCompiler
{
    public MigrationScript Compile(string migrationId, MigrationDirection direction, MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new MigrationScript(
            migrationId,
            direction,
            plan.Operations.SelectMany(operation =>
                Compile(operation).Select(statement =>
                    MigrationOperationConditions.Apply(operation, statement))).ToArray());
    }

    private static IEnumerable<MigrationStatement> Compile(MigrationOperation operation) =>
        operation switch
        {
            CreateTableOperation createTable => CompileCreateTable(createTable),
            DropTableOperation dropTable => Single($"DROP TABLE {Quote(dropTable.Table)}"),
            RenameTableOperation renameTable => Single(
                $"ALTER TABLE {Quote(renameTable.Table)} RENAME TO {Quote(RenameTarget(renameTable.NewName))}"),
            AddColumnOperation addColumn => CompileAddColumn(addColumn),
            DropColumnOperation dropColumn => Single(
                $"ALTER TABLE {Quote(dropColumn.Table)} DROP COLUMN {Quote(dropColumn.Column)}"),
            RenameColumnOperation renameColumn => Single(
                $"ALTER TABLE {Quote(renameColumn.Table)} RENAME COLUMN {Quote(renameColumn.Column)} TO {Quote(renameColumn.NewName)}"),
            CreateIndexOperation createIndex => Single(
                CompileIndex(createIndex.Table, createIndex.Index, ifNotExists: false)),
            DropIndexOperation dropIndex => Single($"DROP INDEX {Quote(dropIndex.Index)}"),
            AlterColumnOperation => throw UnsupportedTableRebuild("alter a column"),
            SetColumnDefaultOperation => throw UnsupportedTableRebuild("set a column default"),
            DropColumnDefaultOperation => throw UnsupportedTableRebuild("drop a column default"),
            AddForeignKeyOperation => throw UnsupportedTableRebuild("add a foreign key"),
            DropForeignKeyOperation => throw UnsupportedTableRebuild("drop a foreign key"),
            AddCheckConstraintOperation => throw UnsupportedTableRebuild("add a check constraint"),
            DropCheckConstraintOperation => throw UnsupportedTableRebuild("drop a check constraint"),
            ExecuteSqlOperation executeSql => Single(executeSql.Sql),
            _ => throw new NotSupportedException(
                $"SQLite does not support migration operation '{operation.GetType().Name}'.")
        };

    private static IEnumerable<MigrationStatement> CompileAddColumn(
        AddColumnOperation operation)
    {
        if (operation.Column.IsPrimaryKey ||
            operation.Column.IsUnique ||
            operation.Column.IsIdentity)
        {
            throw new NotSupportedException(
                "SQLite ADD COLUMN cannot add PRIMARY KEY, UNIQUE, or identity constraints. " +
                "Use a table-rebuild migration or raw SQL.");
        }

        return Single(
            $"ALTER TABLE {Quote(operation.Table)} ADD COLUMN {CompileColumn(operation.Column, false)}");
    }

    private static IEnumerable<MigrationStatement> CompileCreateTable(CreateTableOperation operation)
    {
        if (operation.Columns.Count == 0)
        {
            throw new InvalidOperationException(
                $"Table '{operation.Table}' must define at least one column.");
        }

        var primaryKeys = operation.Columns.Where(column => column.IsPrimaryKey).ToArray();
        var identities = operation.Columns.Where(column => column.IsIdentity).ToArray();
        if (identities.Length > 1)
        {
            throw new InvalidOperationException(
                $"SQLite table '{operation.Table}' cannot define more than one identity column.");
        }

        if (identities.Length == 1 &&
            (!identities[0].IsPrimaryKey || identities[0].IsNullable || primaryKeys.Length != 1 ||
             identities[0].Type is not (MigrationColumnType.Int16 or MigrationColumnType.Int32 or MigrationColumnType.Int64)))
        {
            throw new InvalidOperationException(
                $"SQLite identity column '{identities[0].Name}' must be the single non-nullable primary key.");
        }

        var definitions = operation.Columns
            .Select(column => CompileColumn(column, primaryKeys.Length == 1))
            .ToList();

        if (primaryKeys.Length > 1)
        {
            definitions.Add(
                $"PRIMARY KEY ({string.Join(", ", primaryKeys.Select(column => Quote(column.Name)))})");
        }

        definitions.AddRange(operation.ForeignKeys.Select(CompileForeignKey));
        definitions.AddRange(operation.Checks.Select(CompileCheck));

        var createSql = new StringBuilder()
            .Append("CREATE TABLE ")
            .Append(operation.IfNotExists ? "IF NOT EXISTS " : string.Empty)
            .Append(Quote(operation.Table))
            .AppendLine(" (")
            .Append("    ")
            .Append(string.Join("," + Environment.NewLine + "    ", definitions))
            .AppendLine()
            .Append(')')
            .ToString();

        yield return new MigrationStatement(createSql);
        foreach (var index in operation.Indexes)
        {
            yield return new MigrationStatement(CompileIndex(
                operation.Table, index, operation.IfNotExists));
        }
    }

    private static string CompileColumn(ColumnDefinition column, bool inlinePrimaryKey)
    {
        var sql = new StringBuilder()
            .Append(Quote(column.Name))
            .Append(' ')
            .Append(StoreType(column.Type));

        if (inlinePrimaryKey && column.IsPrimaryKey)
        {
            sql.Append(" PRIMARY KEY");
        }
        if (column.IsIdentity)
        {
            sql.Append(" AUTOINCREMENT");
        }
        if (!column.IsNullable && !column.IsIdentity)
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

    private static string CompileForeignKey(ForeignKeyDefinition foreignKey)
    {
        var sql = new StringBuilder();
        if (foreignKey.Name is not null)
        {
            sql.Append("CONSTRAINT ").Append(Quote(foreignKey.Name)).Append(' ');
        }

        sql.Append("FOREIGN KEY (")
            .Append(string.Join(", ", foreignKey.Columns.Select(Quote)))
            .Append(") REFERENCES ")
            .Append(Quote(foreignKey.ReferencedTable))
            .Append(" (")
            .Append(string.Join(", ", foreignKey.ReferencedColumns.Select(Quote)))
            .Append(')');
        AppendReferentialAction(sql, "UPDATE", foreignKey.OnUpdate);
        AppendReferentialAction(sql, "DELETE", foreignKey.OnDelete);
        return sql.ToString();
    }

    private static string CompileCheck(CheckConstraintDefinition check) =>
        $"CONSTRAINT {Quote(check.Name)} CHECK ({check.Predicate})";

    private static string CompileIndex(
        string table,
        IndexDefinition index,
        bool ifNotExists)
    {
        var name = index.Name ?? DefaultIndexName(table, index);
        var columns = string.Join(", ", index.Columns.Select(column =>
            $"{Quote(column.Name)} {(column.Order == MigrationSortOrder.Descending ? "DESC" : "ASC")}"));
        return $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX {(ifNotExists ? "IF NOT EXISTS " : string.Empty)}{Quote(name)} ON {Quote(table)} ({columns})";
    }

    private static string DefaultIndexName(string table, IndexDefinition index) =>
        $"{(index.IsUnique ? "UX" : "IX")}_{table}_{string.Join("_", index.Columns.Select(column => column.Name))}";

    private static void AppendReferentialAction(
        StringBuilder sql,
        string operation,
        ReferentialAction action)
    {
        if (action == ReferentialAction.NoAction)
        {
            return;
        }

        sql.Append(" ON ").Append(operation).Append(' ').Append(action switch
        {
            ReferentialAction.Restrict => "RESTRICT",
            ReferentialAction.Cascade => "CASCADE",
            ReferentialAction.SetNull => "SET NULL",
            ReferentialAction.SetDefault => "SET DEFAULT",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        });
    }

    private static string StoreType(MigrationColumnType type) => type switch
    {
        MigrationColumnType.Int16 or
        MigrationColumnType.Int32 or
        MigrationColumnType.Int64 or
        MigrationColumnType.Boolean => "INTEGER",
        MigrationColumnType.Decimal or
        MigrationColumnType.Float or
        MigrationColumnType.Double => "REAL",
        MigrationColumnType.Binary => "BLOB",
        MigrationColumnType.String or
        MigrationColumnType.Text or
        MigrationColumnType.Guid or
        MigrationColumnType.Date or
        MigrationColumnType.Time or
        MigrationColumnType.DateTime or
        MigrationColumnType.DateTimeOffset or
        MigrationColumnType.Json => "TEXT",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static string FormatDefault(object value) => value switch
    {
        SqlDefault sql => sql.Sql,
        string text => QuoteLiteral(text),
        char character => QuoteLiteral(character.ToString()),
        bool boolean => boolean ? "1" : "0",
        Guid guid => QuoteLiteral(guid.ToString()),
        DateTime dateTime => QuoteLiteral(dateTime.ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset offset => QuoteLiteral(offset.ToString("O", CultureInfo.InvariantCulture)),
        DateOnly date => QuoteLiteral(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        TimeOnly time => QuoteLiteral(time.ToString("O", CultureInfo.InvariantCulture)),
        byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
        Enum enumeration => Convert.ToInt64(enumeration, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
            ?? throw UnsupportedDefault(value),
        _ => throw UnsupportedDefault(value)
    };

    private static NotSupportedException UnsupportedDefault(object value) => new(
        $"SQLite cannot format a default value of type '{value.GetType().Name}'. Use SqlDefault for SQL expressions.");

    private static NotSupportedException UnsupportedTableRebuild(string operation) => new(
        $"SQLite cannot {operation} directly. Use an explicit table-rebuild migration or raw SQL.");

    private static string QuoteLiteral(string value) => $"'{value.Replace("'", "''")}'";

    private static string RenameTarget(string newName)
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

    private static IEnumerable<MigrationStatement> Single(string sql)
    {
        yield return new MigrationStatement(sql);
    }

    private static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
