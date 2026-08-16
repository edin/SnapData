namespace SnapData.Schema;

public sealed class SqliteSchemaInspector : SchemaInspector
{
    public SqliteSchemaInspector(SnapDatabase database) : base(database)
    {
    }

    public SqliteSchemaInspector(IDbExecutor executor) : base(executor)
    {
    }

    public override async Task<IReadOnlyList<SchemaObjectInfo>> GetObjectsAsync(
        string? schema = null,
        bool includeSystemObjects = false,
        CancellationToken cancellationToken = default)
    {
        if (schema is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        }

        var rows = await QueryAsync<ObjectRow>(
            """
            SELECT schema AS SchemaName, name AS Name, type AS Type
            FROM pragma_table_list
            WHERE (@schema IS NULL OR schema = @schema)
              AND type IN ('table', 'virtual', 'view')
              AND (@includeSystem = 1 OR name NOT LIKE 'sqlite_%')
            ORDER BY schema, type, name
            """,
            new
            {
                schema,
                includeSystem = includeSystemObjects ? 1 : 0
            },
            cancellationToken);

        return rows
            .Select(row => new SchemaObjectInfo(
                new SchemaObjectName(row.Name, row.SchemaName),
                row.Type.Equals("view", StringComparison.OrdinalIgnoreCase)
                    ? SchemaObjectKind.View
                    : SchemaObjectKind.Table,
                row.Name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public override async Task<TableSchema?> GetTableAsync(
        SchemaObjectName table,
        SchemaReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!await TableExistsAsync(table, cancellationToken))
        {
            return null;
        }

        options ??= SchemaReadOptions.Default;
        var definition = options.IncludeDefinitionSql || options.IncludeColumns
            ? await ReadDefinitionSqlAsync(table, "table", cancellationToken)
            : null;
        var columns = options.IncludeColumns || options.IncludePrimaryKeys
            ? await ReadColumnsAsync(table, cancellationToken)
            : [];
        var primaryKey = options.IncludePrimaryKeys
            ? CreatePrimaryKey(columns)
            : null;
        var foreignKeys = options.IncludeForeignKeys
            ? await ReadForeignKeysAsync(table, cancellationToken)
            : [];
        var indexes = options.IncludeIndexes
            ? await ReadIndexesAsync(table, options.IncludeDefinitionSql, cancellationToken)
            : [];

        return new TableSchema(
            table,
            options.IncludeColumns ? CreateColumnSchemas(columns, definition) : [],
            primaryKey,
            foreignKeys,
            indexes,
            definitionSql: options.IncludeDefinitionSql ? definition : null);
    }

    public override async Task<DatabaseSchema> ReadAsync(
        SchemaReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= SchemaReadOptions.Default;
        var objects = await GetObjectsAsync(
            schema: "main",
            includeSystemObjects: false,
            cancellationToken: cancellationToken);
        var tables = new List<TableSchema>();
        foreach (var item in objects.Where(item => item.Kind == SchemaObjectKind.Table))
        {
            var table = await GetTableAsync(item.Name, options, cancellationToken);
            if (table is not null)
            {
                tables.Add(table);
            }
        }

        var views = new List<ViewSchema>();
        if (options.IncludeViews)
        {
            foreach (var item in objects.Where(item => item.Kind == SchemaObjectKind.View))
            {
                var columns = options.IncludeColumns
                    ? CreateColumnSchemas(
                        await ReadColumnsAsync(item.Name, cancellationToken),
                        definitionSql: null,
                        allowIdentity: false)
                    : [];
                views.Add(new ViewSchema(
                    item.Name,
                    columns,
                    options.IncludeDefinitionSql
                        ? await ReadDefinitionSqlAsync(item.Name, "view", cancellationToken)
                        : null));
            }
        }

        return new DatabaseSchema("main", tables, views);
    }

    public override async Task<bool> TableExistsAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        return await ScalarAsync<long>(
            """
            SELECT COUNT(*)
            FROM pragma_table_list
            WHERE type IN ('table', 'virtual')
              AND schema = @schema COLLATE NOCASE
              AND name = @name COLLATE NOCASE
            """,
            new { schema = table.Schema ?? "main", name = table.Name },
            cancellationToken) > 0;
    }

    public override async Task<bool> ColumnExistsAsync(
        SchemaObjectName table,
        string column,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return await ScalarAsync<long>(
            """
            SELECT COUNT(*)
            FROM pragma_table_xinfo(@table, @schema)
            WHERE name = @column COLLATE NOCASE
            """,
            new
            {
                table = table.Name,
                schema = table.Schema ?? "main",
                column
            },
            cancellationToken) > 0;
    }

    private async Task<IReadOnlyList<ColumnRow>> ReadColumnsAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken) =>
        await QueryAsync<ColumnRow>(
            """
            SELECT
                cid AS "Ordinal",
                name AS "Name",
                type AS "StoreType",
                "notnull" AS "NotNull",
                dflt_value AS "DefaultExpression",
                pk AS "PrimaryKeyOrdinal",
                hidden AS "Hidden"
            FROM pragma_table_xinfo(@table, @schema)
            ORDER BY cid
            """,
            new { table = table.Name, schema = table.Schema ?? "main" },
            cancellationToken);

    private async Task<IReadOnlyList<ForeignKeySchema>> ReadForeignKeysAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<ForeignKeyRow>(
            """
            SELECT
                id AS "Id",
                seq AS "Sequence",
                "table" AS "ReferencedTable",
                "from" AS "FromColumn",
                "to" AS "ToColumn",
                on_update AS "OnUpdate",
                on_delete AS "OnDelete"
            FROM pragma_foreign_key_list(@table, @schema)
            ORDER BY id, seq
            """,
            new { table = table.Name, schema = table.Schema ?? "main" },
            cancellationToken);

        var result = new List<ForeignKeySchema>();
        foreach (var group in rows.GroupBy(row => row.Id).OrderBy(group => group.Key))
        {
            var ordered = group.OrderBy(row => row.Sequence).ToArray();
            var referencedName = new SchemaObjectName(
                ordered[0].ReferencedTable,
                table.Schema);
            var referencedColumns = ordered.Select(row => row.ToColumn).ToArray();
            if (referencedColumns.Any(column => string.IsNullOrWhiteSpace(column)))
            {
                var parentColumns = await ReadColumnsAsync(referencedName, cancellationToken);
                var parentKey = CreatePrimaryKey(parentColumns)
                    ?? throw new InvalidOperationException(
                        $"SQLite foreign key on {table} references {referencedName} without explicit columns, but the referenced table has no primary key.");
                if (parentKey.Columns.Count != ordered.Length)
                {
                    throw new InvalidOperationException(
                        $"SQLite foreign key on {table} has {ordered.Length} columns but referenced primary key {referencedName} has {parentKey.Columns.Count} columns.");
                }

                referencedColumns = parentKey.Columns.Cast<string?>().ToArray();
            }

            result.Add(new ForeignKeySchema(
                name: null,
                ordered.Select(row => row.FromColumn),
                referencedName,
                referencedColumns.Select(column => column!),
                ParseReferentialAction(ordered[0].OnUpdate),
                ParseReferentialAction(ordered[0].OnDelete)));
        }

        return result;
    }

    private async Task<IReadOnlyList<IndexSchema>> ReadIndexesAsync(
        SchemaObjectName table,
        bool includeDefinitionSql,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<IndexRow>(
            """
            SELECT
                name AS "Name",
                "unique" AS "IsUnique",
                origin AS "Origin",
                partial AS "IsPartial"
            FROM pragma_index_list(@table, @schema)
            ORDER BY seq
            """,
            new { table = table.Name, schema = table.Schema ?? "main" },
            cancellationToken);

        var indexes = new List<IndexSchema>(rows.Count);
        foreach (var row in rows)
        {
            var definition = await ReadDefinitionSqlAsync(
                new SchemaObjectName(row.Name, table.Schema),
                "index",
                cancellationToken);
            var parsed = ParseIndexDefinition(definition);
            var columnRows = await QueryAsync<IndexColumnRow>(
                """
                SELECT
                    seqno AS "Ordinal",
                    cid AS "ColumnId",
                    name AS "Name",
                    "desc" AS "Descending"
                FROM pragma_index_xinfo(@index, @schema)
                WHERE "key" = 1
                ORDER BY seqno
                """,
                new { index = row.Name, schema = table.Schema ?? "main" },
                cancellationToken);
            var columns = columnRows.Select(column => new IndexColumnSchema(
                column.ColumnId >= 0 ? column.Name : null,
                column.Ordinal,
                column.Descending != 0,
                column.ColumnId == -2
                    ? parsed.Terms.ElementAtOrDefault(column.Ordinal)
                    : null));

            indexes.Add(new IndexSchema(
                row.Name,
                columns,
                row.IsUnique != 0,
                row.IsPartial != 0 ? parsed.FilterExpression : null,
                ParseIndexOrigin(row.Origin),
                includeDefinitionSql ? definition : null));
        }

        return indexes;
    }

    private async Task<string?> ReadDefinitionSqlAsync(
        SchemaObjectName item,
        string type,
        CancellationToken cancellationToken)
    {
        if (item.Schema is not null &&
            !item.Schema.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await ScalarAsync<string?>(
            """
            SELECT sql
            FROM sqlite_schema
            WHERE type = @type AND name = @name COLLATE NOCASE
            """,
            new { type, name = item.Name },
            cancellationToken);
    }

    private static PrimaryKeySchema? CreatePrimaryKey(IReadOnlyList<ColumnRow> columns)
    {
        var names = columns
            .Where(column => column.PrimaryKeyOrdinal > 0)
            .OrderBy(column => column.PrimaryKeyOrdinal)
            .Select(column => column.Name)
            .ToArray();
        return names.Length == 0 ? null : new PrimaryKeySchema(null, names);
    }

    private static IReadOnlyList<ColumnSchema> CreateColumnSchemas(
        IReadOnlyList<ColumnRow> columns,
        string? definitionSql,
        bool allowIdentity = true)
    {
        var primaryKeyColumns = columns
            .Where(column => column.PrimaryKeyOrdinal > 0)
            .ToArray();
        var identity = allowIdentity
            && primaryKeyColumns.Length == 1
            && primaryKeyColumns[0].StoreType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)
            && !ContainsSql(definitionSql, "WITHOUT ROWID")
                ? primaryKeyColumns[0].Name
                : null;
        var autoIncrement = identity is not null && ContainsSql(definitionSql, "AUTOINCREMENT");

        return columns
            .Select(column => column.CreateSchema(
                isIdentity: string.Equals(column.Name, identity, StringComparison.OrdinalIgnoreCase),
                isAutoIncrement: autoIncrement && string.Equals(
                    column.Name,
                    identity,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static bool ContainsSql(string? sql, string value) =>
        sql?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

    private static ReferentialAction ParseReferentialAction(string value) =>
        value.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "NOACTION" => ReferentialAction.NoAction,
            "RESTRICT" => ReferentialAction.Restrict,
            "CASCADE" => ReferentialAction.Cascade,
            "SETNULL" => ReferentialAction.SetNull,
            "SETDEFAULT" => ReferentialAction.SetDefault,
            _ => throw new InvalidOperationException(
                $"SQLite returned unsupported referential action '{value}'.")
        };

    private static SchemaIndexOrigin ParseIndexOrigin(string value) => value switch
    {
        "c" => SchemaIndexOrigin.Created,
        "u" => SchemaIndexOrigin.UniqueConstraint,
        "pk" => SchemaIndexOrigin.PrimaryKey,
        _ => throw new InvalidOperationException(
            $"SQLite returned unsupported index origin '{value}'.")
    };

    private static ParsedIndexDefinition ParseIndexDefinition(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new ParsedIndexDefinition([], null);
        }

        var open = sql.IndexOf('(');
        if (open < 0)
        {
            return new ParsedIndexDefinition([], null);
        }

        var close = FindClosingParenthesis(sql, open);
        if (close < 0)
        {
            return new ParsedIndexDefinition([], null);
        }

        var terms = SplitTopLevel(sql[(open + 1)..close])
            .Select(RemoveIndexTermModifiers)
            .ToArray();
        var remainder = sql[(close + 1)..].Trim();
        var filter = remainder.StartsWith("WHERE ", StringComparison.OrdinalIgnoreCase)
            ? remainder[6..].Trim()
            : null;
        return new ParsedIndexDefinition(terms, filter);
    }

    private static int FindClosingParenthesis(string value, int open)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = open; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    if (index + 1 < value.Length && value[index + 1] == quote)
                    {
                        index++;
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (character is '\'' or '"' or '`')
            {
                quote = character;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitTopLevel(string value)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    if (index + 1 < value.Length && value[index + 1] == quote)
                    {
                        index++;
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (character is '\'' or '"' or '`')
            {
                quote = character;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                result.Add(value[start..index].Trim());
                start = index + 1;
            }
        }

        result.Add(value[start..].Trim());
        return result;
    }

    private static string RemoveIndexTermModifiers(string term)
    {
        var result = term.Trim();
        if (result.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase))
        {
            result = result[..^5].TrimEnd();
        }
        else if (result.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase))
        {
            result = result[..^4].TrimEnd();
        }

        return result;
    }

    private sealed class ObjectRow
    {
        public required string SchemaName { get; init; }

        public required string Name { get; init; }

        public required string Type { get; init; }
    }

    private sealed class ColumnRow
    {
        public int Ordinal { get; init; }

        public required string Name { get; init; }

        public string StoreType { get; init; } = "";

        public int NotNull { get; init; }

        public string? DefaultExpression { get; init; }

        public int PrimaryKeyOrdinal { get; init; }

        public int Hidden { get; init; }

        public ColumnSchema CreateSchema(bool isIdentity, bool isAutoIncrement)
        {
            var type = SqliteTypeMapping.Resolve(StoreType);
            return new ColumnSchema(
                Name,
                Ordinal,
                StoreType,
                type.DbType,
                type.ClrType,
                isNullable: !isIdentity && NotNull == 0,
                generatedKind: isIdentity
                    ? SchemaGeneratedKind.Identity
                    : Hidden switch
                    {
                        1 => SchemaGeneratedKind.Hidden,
                        2 => SchemaGeneratedKind.ComputedVirtual,
                        3 => SchemaGeneratedKind.ComputedStored,
                        _ => SchemaGeneratedKind.None
                    },
                DefaultExpression,
                type.Affinity,
                isAutoIncrement);
        }
    }

    private sealed class ForeignKeyRow
    {
        public int Id { get; init; }

        public int Sequence { get; init; }

        public required string ReferencedTable { get; init; }

        public required string FromColumn { get; init; }

        public string? ToColumn { get; init; }

        public required string OnUpdate { get; init; }

        public required string OnDelete { get; init; }
    }

    private sealed class IndexRow
    {
        public required string Name { get; init; }

        public int IsUnique { get; init; }

        public required string Origin { get; init; }

        public int IsPartial { get; init; }
    }

    private sealed class IndexColumnRow
    {
        public int Ordinal { get; init; }

        public int ColumnId { get; init; }

        public string? Name { get; init; }

        public int Descending { get; init; }
    }

    private sealed record ParsedIndexDefinition(
        IReadOnlyList<string> Terms,
        string? FilterExpression);

}
