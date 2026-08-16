namespace SnapData.Schema;

public sealed class MySqlSchemaInspector : SchemaInspector
{
    public MySqlSchemaInspector(SnapDatabase database) : base(database)
    {
    }

    public MySqlSchemaInspector(IDbExecutor executor) : base(executor)
    {
    }

    public override async Task<bool> TableExistsAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        return await ScalarAsync<long>(
            """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = COALESCE(@schema, DATABASE())
              AND table_name = @name AND table_type = 'BASE TABLE'
            """,
            new { schema = table.Schema, name = table.Name },
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
            FROM information_schema.columns
            WHERE table_schema = COALESCE(@schema, DATABASE())
              AND table_name = @table AND column_name = @column
            """,
            new { schema = table.Schema, table = table.Name, column },
            cancellationToken) > 0;
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
            SELECT table_schema AS SchemaName, table_name AS Name,
                   table_type AS TableType,
                   table_schema IN ('information_schema', 'mysql', 'performance_schema', 'sys')
                       AS IsSystemObject
            FROM information_schema.tables
            WHERE table_schema = COALESCE(@schema, DATABASE())
              AND (@includeSystem = 1 OR table_schema NOT IN
                  ('information_schema', 'mysql', 'performance_schema', 'sys'))
            ORDER BY table_type, table_name
            """,
            new { schema, includeSystem = includeSystemObjects },
            cancellationToken);

        return rows.Select(row => new SchemaObjectInfo(
            new SchemaObjectName(row.Name, row.SchemaName),
            row.TableType.Equals("VIEW", StringComparison.OrdinalIgnoreCase)
                ? SchemaObjectKind.View
                : SchemaObjectKind.Table,
            row.IsSystemObject)).ToArray();
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
        var schema = table.Schema ?? await CurrentDatabaseAsync(cancellationToken);
        var resolved = new SchemaObjectName(table.Name, schema);
        var primaryKey = options.IncludePrimaryKeys
            ? await ReadPrimaryKeyAsync(resolved, cancellationToken)
            : null;
        var foreignKeys = options.IncludeForeignKeys
            ? await ReadForeignKeysAsync(resolved, cancellationToken)
            : [];
        var indexes = options.IncludeIndexes
            ? await ReadIndexesAsync(resolved, cancellationToken)
            : [];
        return new TableSchema(
            resolved,
            options.IncludeColumns ? await ReadColumnsAsync(resolved, cancellationToken) : [],
            primaryKey,
            foreignKeys,
            indexes,
            definitionSql: null);
    }

    public override async Task<DatabaseSchema> ReadAsync(
        SchemaReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= SchemaReadOptions.Default;
        var databaseName = await CurrentDatabaseAsync(cancellationToken);
        var objects = await GetObjectsAsync(databaseName, cancellationToken: cancellationToken);
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
                views.Add(new ViewSchema(
                    item.Name,
                    options.IncludeColumns
                        ? await ReadColumnsAsync(item.Name, cancellationToken)
                        : [],
                    options.IncludeDefinitionSql
                        ? await ReadViewDefinitionAsync(item.Name, cancellationToken)
                        : null));
            }
        }

        return new DatabaseSchema(databaseName, tables, views);
    }

    private async Task<IReadOnlyList<ColumnSchema>> ReadColumnsAsync(
        SchemaObjectName item,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<ColumnRow>(
            """
            SELECT ordinal_position - 1 AS Ordinal, column_name AS Name,
                   column_type AS StoreType, data_type AS DataType,
                   is_nullable = 'YES' AS IsNullable,
                   column_default AS DefaultExpression, extra AS Extra
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @name
            ORDER BY ordinal_position
            """,
            new { schema = item.Schema, name = item.Name },
            cancellationToken);

        return rows.Select(row => row.ToSchema()).ToArray();
    }

    private async Task<string?> ReadViewDefinitionAsync(
        SchemaObjectName view,
        CancellationToken cancellationToken) =>
        await ScalarAsync<string?>(
            """
            SELECT view_definition
            FROM information_schema.views
            WHERE table_schema = @schema AND table_name = @name
            """,
            new { schema = view.Schema, name = view.Name },
            cancellationToken);

    private async Task<PrimaryKeySchema?> ReadPrimaryKeyAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<KeyColumnRow>(
            """
            SELECT tc.constraint_name AS ConstraintName,
                   kcu.column_name AS ColumnName,
                   kcu.ordinal_position AS Ordinal
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON kcu.constraint_schema = tc.constraint_schema
             AND kcu.table_name = tc.table_name
             AND kcu.constraint_name = tc.constraint_name
            WHERE tc.constraint_type = 'PRIMARY KEY'
              AND tc.table_schema = @schema AND tc.table_name = @name
            ORDER BY kcu.ordinal_position
            """,
            new { schema = table.Schema, name = table.Name },
            cancellationToken);

        return rows.Count == 0
            ? null
            : new PrimaryKeySchema(rows[0].ConstraintName, rows.Select(row => row.ColumnName));
    }

    private async Task<IReadOnlyList<ForeignKeySchema>> ReadForeignKeysAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<ForeignKeyRow>(
            """
            SELECT kcu.constraint_name AS Name,
                   kcu.ordinal_position AS Ordinal,
                   kcu.column_name AS ColumnName,
                   kcu.referenced_table_schema AS ReferencedSchema,
                   kcu.referenced_table_name AS ReferencedTable,
                   kcu.referenced_column_name AS ReferencedColumn,
                   rc.update_rule AS OnUpdate, rc.delete_rule AS OnDelete
            FROM information_schema.key_column_usage kcu
            JOIN information_schema.referential_constraints rc
              ON rc.constraint_schema = kcu.constraint_schema
             AND rc.table_name = kcu.table_name
             AND rc.constraint_name = kcu.constraint_name
            WHERE kcu.table_schema = @schema AND kcu.table_name = @name
              AND kcu.referenced_table_name IS NOT NULL
            ORDER BY kcu.constraint_name, kcu.ordinal_position
            """,
            new { schema = table.Schema, name = table.Name },
            cancellationToken);

        return rows
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var ordered = group.OrderBy(row => row.Ordinal).ToArray();
                return new ForeignKeySchema(
                    first.Name,
                    ordered.Select(row => row.ColumnName),
                    new SchemaObjectName(first.ReferencedTable, first.ReferencedSchema),
                    ordered.Select(row => row.ReferencedColumn),
                    ParseReferentialAction(first.OnUpdate),
                    ParseReferentialAction(first.OnDelete));
            })
            .ToArray();
    }

    private static ReferentialAction ParseReferentialAction(string value) =>
        value.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "NOACTION" => ReferentialAction.NoAction,
            "RESTRICT" => ReferentialAction.Restrict,
            "CASCADE" => ReferentialAction.Cascade,
            "SETNULL" => ReferentialAction.SetNull,
            "SETDEFAULT" => ReferentialAction.SetDefault,
            _ => throw new InvalidOperationException(
                $"MySQL returned unsupported referential action '{value}'.")
        };

    private async Task<IReadOnlyList<IndexSchema>> ReadIndexesAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<IndexRow>(
            """
            SELECT s.index_name AS Name, s.non_unique = 0 AS IsUnique,
                   s.seq_in_index - 1 AS Ordinal, s.column_name AS ColumnName,
                   s.expression AS Expression,
                   COALESCE(s.collation = 'D', false) AS IsDescending,
                   s.sub_part AS PrefixLength, s.index_type AS Method,
                   s.is_visible = 'YES' AS IsVisible,
                   COALESCE(tc.constraint_type = 'UNIQUE', false) AS IsUniqueConstraint
            FROM information_schema.statistics s
            LEFT JOIN information_schema.table_constraints tc
              ON tc.constraint_schema = s.table_schema
             AND tc.table_name = s.table_name
             AND tc.constraint_name = s.index_name
             AND tc.constraint_type = 'UNIQUE'
            WHERE s.table_schema = @schema AND s.table_name = @name
            ORDER BY s.index_name, s.seq_in_index
            """,
            new { schema = table.Schema, name = table.Name },
            cancellationToken);

        return rows
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new IndexSchema(
                    first.Name,
                    group.OrderBy(row => row.Ordinal).Select(row =>
                        new IndexColumnSchema(
                            row.ColumnName,
                            row.Ordinal,
                            row.IsDescending,
                            row.Expression,
                            prefixLength: row.PrefixLength)),
                    first.IsUnique,
                    origin: first.Name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
                        ? SchemaIndexOrigin.PrimaryKey
                        : first.IsUniqueConstraint
                            ? SchemaIndexOrigin.UniqueConstraint
                            : SchemaIndexOrigin.Created,
                    isVisible: first.IsVisible,
                    method: first.Method);
            })
            .ToArray();
    }

    private async Task<string> CurrentDatabaseAsync(CancellationToken cancellationToken) =>
        await ScalarAsync<string>("SELECT DATABASE()", cancellationToken: cancellationToken);

    private sealed class ObjectRow
    {
        public required string SchemaName { get; init; }
        public required string Name { get; init; }
        public required string TableType { get; init; }
        public bool IsSystemObject { get; init; }
    }

    private sealed class KeyColumnRow
    {
        public required string ConstraintName { get; init; }
        public required string ColumnName { get; init; }
        public int Ordinal { get; init; }
    }

    private sealed class ForeignKeyRow
    {
        public required string Name { get; init; }
        public int Ordinal { get; init; }
        public required string ColumnName { get; init; }
        public required string ReferencedSchema { get; init; }
        public required string ReferencedTable { get; init; }
        public required string ReferencedColumn { get; init; }
        public required string OnUpdate { get; init; }
        public required string OnDelete { get; init; }
    }

    private sealed class IndexRow
    {
        public required string Name { get; init; }
        public bool IsUnique { get; init; }
        public int Ordinal { get; init; }
        public string? ColumnName { get; init; }
        public string? Expression { get; init; }
        public bool IsDescending { get; init; }
        public int? PrefixLength { get; init; }
        public required string Method { get; init; }
        public bool IsVisible { get; init; }
        public bool IsUniqueConstraint { get; init; }
    }

    private sealed class ColumnRow
    {
        public int Ordinal { get; init; }
        public required string Name { get; init; }
        public required string StoreType { get; init; }
        public required string DataType { get; init; }
        public bool IsNullable { get; init; }
        public string? DefaultExpression { get; init; }
        public string Extra { get; init; } = "";

        public ColumnSchema ToSchema()
        {
            var type = MySqlTypeMapping.Resolve(DataType, StoreType);
            var isIdentity = Extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase);
            return new ColumnSchema(
                Name,
                Ordinal,
                StoreType,
                type.DbType,
                type.ClrType,
                IsNullable,
                isIdentity
                    ? SchemaGeneratedKind.Identity
                    : Extra.Contains("STORED GENERATED", StringComparison.OrdinalIgnoreCase)
                        ? SchemaGeneratedKind.ComputedStored
                        : Extra.Contains("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase)
                            ? SchemaGeneratedKind.ComputedVirtual
                            : SchemaGeneratedKind.None,
                DefaultExpression,
                isAutoIncrement: isIdentity);
        }
    }
}
