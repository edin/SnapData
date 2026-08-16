namespace SnapData.Schema;

public sealed class PostgresSchemaInspector : SchemaInspector
{
    public PostgresSchemaInspector(SnapDatabase database) : base(database)
    {
    }

    public PostgresSchemaInspector(IDbExecutor executor) : base(executor)
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
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @name
              AND c.relkind IN ('r', 'p')
            """,
            new { schema = table.Schema ?? "public", name = table.Name },
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
            FROM pg_catalog.pg_attribute a
            JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @table
              AND a.attname = @column AND a.attnum > 0 AND NOT a.attisdropped
              AND c.relkind IN ('r', 'p')
            """,
            new { schema = table.Schema ?? "public", table = table.Name, column },
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
            SELECT n.nspname AS SchemaName, c.relname AS Name, c.relkind::text AS Kind,
                   (n.nspname = 'information_schema' OR n.nspname LIKE 'pg_%') AS IsSystemObject
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p', 'v', 'm')
              AND (CAST(@schema AS text) IS NULL OR n.nspname = @schema)
              AND (@includeSystem OR NOT (n.nspname = 'information_schema' OR n.nspname LIKE 'pg_%'))
            ORDER BY n.nspname, c.relkind, c.relname
            """,
            new { schema, includeSystem = includeSystemObjects },
            cancellationToken);

        return rows.Select(row => new SchemaObjectInfo(
            new SchemaObjectName(row.Name, row.SchemaName),
            row.Kind is "v" or "m" ? SchemaObjectKind.View : SchemaObjectKind.Table,
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
        var primaryKey = options.IncludePrimaryKeys
            ? await ReadPrimaryKeyAsync(table, cancellationToken)
            : null;
        var foreignKeys = options.IncludeForeignKeys
            ? await ReadForeignKeysAsync(table, cancellationToken)
            : [];
        var indexes = options.IncludeIndexes
            ? await ReadIndexesAsync(table, options.IncludeDefinitionSql, cancellationToken)
            : [];
        return new TableSchema(
            new SchemaObjectName(table.Name, table.Schema ?? "public"),
            options.IncludeColumns ? await ReadColumnsAsync(table, cancellationToken) : [],
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
        var objects = await GetObjectsAsync(cancellationToken: cancellationToken);
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

        return new DatabaseSchema(
            await ScalarAsync<string>("SELECT current_database()", cancellationToken: cancellationToken),
            tables,
            views);
    }

    private async Task<IReadOnlyList<ColumnSchema>> ReadColumnsAsync(
        SchemaObjectName item,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<ColumnRow>(
            """
            SELECT a.attnum - 1 AS Ordinal, a.attname AS Name,
                   pg_catalog.format_type(a.atttypid, a.atttypmod) AS StoreType,
                   COALESCE(base_type.typname, t.typname) AS SystemTypeName,
                   COALESCE(base_type.typcategory, t.typcategory)::text AS TypeCategory,
                   element_type.typname AS ElementTypeName,
                   a.attnotnull AS IsNotNullable,
                   pg_catalog.pg_get_expr(ad.adbin, ad.adrelid) AS DefaultExpression,
                   a.attidentity::text AS IdentityKind, a.attgenerated::text AS GeneratedKind
            FROM pg_catalog.pg_attribute a
            JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_catalog.pg_type t ON t.oid = a.atttypid
            LEFT JOIN pg_catalog.pg_type base_type ON base_type.oid = t.typbasetype
            LEFT JOIN pg_catalog.pg_type element_type
                ON element_type.oid = CASE
                    WHEN base_type.oid IS NOT NULL THEN base_type.typelem
                    ELSE t.typelem
                END
            LEFT JOIN pg_catalog.pg_attrdef ad
                ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
            WHERE n.nspname = @schema AND c.relname = @name
              AND c.relkind IN ('r', 'p', 'v', 'm')
              AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
            """,
            new { schema = item.Schema ?? "public", name = item.Name },
            cancellationToken);

        return rows.Select(row => row.ToSchema()).ToArray();
    }

    private async Task<string?> ReadViewDefinitionAsync(
        SchemaObjectName view,
        CancellationToken cancellationToken) =>
        await ScalarAsync<string?>(
            """
            SELECT pg_catalog.pg_get_viewdef(c.oid, true)
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @name
              AND c.relkind IN ('v', 'm')
            """,
            new { schema = view.Schema ?? "public", name = view.Name },
            cancellationToken);

    private async Task<PrimaryKeySchema?> ReadPrimaryKeyAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<KeyColumnRow>(
            """
            SELECT con.conname AS ConstraintName, a.attname AS ColumnName,
                   key.ordinality::integer AS Ordinal
            FROM pg_catalog.pg_constraint con
            JOIN pg_catalog.pg_class c ON c.oid = con.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            JOIN LATERAL unnest(con.conkey) WITH ORDINALITY AS key(attnum, ordinality)
                ON true
            JOIN pg_catalog.pg_attribute a
                ON a.attrelid = c.oid AND a.attnum = key.attnum
            WHERE con.contype = 'p' AND n.nspname = @schema AND c.relname = @name
            ORDER BY key.ordinality
            """,
            new { schema = table.Schema ?? "public", name = table.Name },
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
            SELECT con.oid::bigint AS Id, con.conname AS Name,
                   pos.ordinality AS Ordinal, pa.attname AS ColumnName,
                   rn.nspname AS ReferencedSchema, rc.relname AS ReferencedTable,
                   ra.attname AS ReferencedColumn,
                   con.confupdtype::text AS OnUpdate,
                   con.confdeltype::text AS OnDelete
            FROM pg_catalog.pg_constraint con
            JOIN pg_catalog.pg_class pc ON pc.oid = con.conrelid
            JOIN pg_catalog.pg_namespace pn ON pn.oid = pc.relnamespace
            JOIN pg_catalog.pg_class rc ON rc.oid = con.confrelid
            JOIN pg_catalog.pg_namespace rn ON rn.oid = rc.relnamespace
            JOIN LATERAL generate_subscripts(con.conkey, 1)
                WITH ORDINALITY AS pos(index, ordinality) ON true
            JOIN pg_catalog.pg_attribute pa
                ON pa.attrelid = pc.oid AND pa.attnum = con.conkey[pos.index]
            JOIN pg_catalog.pg_attribute ra
                ON ra.attrelid = rc.oid AND ra.attnum = con.confkey[pos.index]
            WHERE con.contype = 'f' AND pn.nspname = @schema AND pc.relname = @name
            ORDER BY con.oid, pos.ordinality
            """,
            new { schema = table.Schema ?? "public", name = table.Name },
            cancellationToken);

        return rows
            .GroupBy(row => row.Id)
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

    private static ReferentialAction ParseReferentialAction(string value) => value switch
    {
        "a" => ReferentialAction.NoAction,
        "r" => ReferentialAction.Restrict,
        "c" => ReferentialAction.Cascade,
        "n" => ReferentialAction.SetNull,
        "d" => ReferentialAction.SetDefault,
        _ => throw new InvalidOperationException(
            $"PostgreSQL returned unsupported referential action '{value}'.")
    };

    private async Task<IReadOnlyList<IndexSchema>> ReadIndexesAsync(
        SchemaObjectName table,
        bool includeDefinitionSql,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<IndexRow>(
            """
            SELECT i.indexrelid::bigint AS Id, ix.relname AS Name,
                   i.indisunique AS IsUnique, i.indisprimary AS IsPrimaryKey,
                   COALESCE(con.contype = 'u', false) AS IsUniqueConstraint,
                   pos.position AS Ordinal, a.attname AS ColumnName,
                   pg_catalog.pg_get_indexdef(i.indexrelid, pos.position + 1, true)
                       AS TermDefinition,
                   CASE WHEN pos.position < i.indnkeyatts
                       THEN (i.indoption[pos.position] & 1) = 1
                       ELSE false
                   END AS IsDescending,
                   pos.position >= i.indnkeyatts AS IsIncluded,
                   pg_catalog.pg_get_expr(i.indpred, i.indrelid) AS FilterExpression,
                   pg_catalog.pg_get_indexdef(i.indexrelid) AS DefinitionSql
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class t ON t.oid = i.indrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_catalog.pg_class ix ON ix.oid = i.indexrelid
            JOIN LATERAL generate_series(0, i.indnatts - 1) AS pos(position) ON true
            LEFT JOIN pg_catalog.pg_attribute a
                ON a.attrelid = t.oid AND a.attnum = i.indkey[pos.position]
            LEFT JOIN pg_catalog.pg_constraint con
                ON con.conindid = i.indexrelid AND con.contype = 'u'
            WHERE n.nspname = @schema AND t.relname = @name
              AND i.indisvalid AND i.indisready
            ORDER BY i.indexrelid, pos.position
            """,
            new { schema = table.Schema ?? "public", name = table.Name },
            cancellationToken);

        return rows
            .GroupBy(row => row.Id)
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
                            expression: row.ColumnName is null
                                ? RemoveIndexTermModifiers(row.TermDefinition)
                                : null,
                            isIncluded: row.IsIncluded)),
                    first.IsUnique,
                    first.FilterExpression,
                    first.IsPrimaryKey
                        ? SchemaIndexOrigin.PrimaryKey
                        : first.IsUniqueConstraint
                            ? SchemaIndexOrigin.UniqueConstraint
                            : SchemaIndexOrigin.Created,
                    includeDefinitionSql ? first.DefinitionSql : null);
            })
            .ToArray();
    }

    private static string RemoveIndexTermModifiers(string term)
    {
        var result = term.Trim();
        string[] modifiers = [" NULLS FIRST", " NULLS LAST", " DESC", " ASC"];
        var removed = true;
        while (removed)
        {
            removed = false;
            foreach (var modifier in modifiers)
            {
                if (result.EndsWith(modifier, StringComparison.OrdinalIgnoreCase))
                {
                    result = result[..^modifier.Length].TrimEnd();
                    removed = true;
                    break;
                }
            }
        }

        return result;
    }

    private sealed class ObjectRow
    {
        public required string SchemaName { get; init; }
        public required string Name { get; init; }
        public required string Kind { get; init; }
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
        public long Id { get; init; }
        public required string Name { get; init; }
        public long Ordinal { get; init; }
        public required string ColumnName { get; init; }
        public required string ReferencedSchema { get; init; }
        public required string ReferencedTable { get; init; }
        public required string ReferencedColumn { get; init; }
        public required string OnUpdate { get; init; }
        public required string OnDelete { get; init; }
    }

    private sealed class IndexRow
    {
        public long Id { get; init; }
        public required string Name { get; init; }
        public bool IsUnique { get; init; }
        public bool IsPrimaryKey { get; init; }
        public bool IsUniqueConstraint { get; init; }
        public int Ordinal { get; init; }
        public string? ColumnName { get; init; }
        public required string TermDefinition { get; init; }
        public bool IsDescending { get; init; }
        public bool IsIncluded { get; init; }
        public string? FilterExpression { get; init; }
        public required string DefinitionSql { get; init; }
    }

    private sealed class ColumnRow
    {
        public int Ordinal { get; init; }
        public required string Name { get; init; }
        public required string StoreType { get; init; }
        public required string SystemTypeName { get; init; }
        public required string TypeCategory { get; init; }
        public string? ElementTypeName { get; init; }
        public bool IsNotNullable { get; init; }
        public string? DefaultExpression { get; init; }
        public string IdentityKind { get; init; } = "";
        public string GeneratedKind { get; init; } = "";

        public ColumnSchema ToSchema()
        {
            var type = PostgresTypeMapping.Resolve(
                SystemTypeName,
                TypeCategory,
                ElementTypeName);
            var isSequence = DefaultExpression?.StartsWith(
                "nextval(",
                StringComparison.OrdinalIgnoreCase) == true;
            var isIdentity = IdentityKind.Length > 0 || isSequence;
            return new ColumnSchema(
                Name,
                Ordinal,
                StoreType,
                type.DbType,
                type.ClrType,
                !IsNotNullable,
                isIdentity
                    ? SchemaGeneratedKind.Identity
                    : GeneratedKind switch
                    {
                        "s" => SchemaGeneratedKind.ComputedStored,
                        "v" => SchemaGeneratedKind.ComputedVirtual,
                        _ => SchemaGeneratedKind.None
                    },
                DefaultExpression,
                isAutoIncrement: isIdentity);
        }
    }
}
