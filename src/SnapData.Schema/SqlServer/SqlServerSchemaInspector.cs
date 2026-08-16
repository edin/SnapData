namespace SnapData.Schema;

public sealed class SqlServerSchemaInspector : SchemaInspector
{
    public SqlServerSchemaInspector(SnapDatabase database) : base(database)
    {
    }

    public SqlServerSchemaInspector(IDbExecutor executor) : base(executor)
    {
    }

    public override async Task<bool> TableExistsAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        return await ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND t.name = @name
            """,
            new { schema = table.Schema ?? "dbo", name = table.Name },
            cancellationToken) > 0;
    }

    public override async Task<bool> ColumnExistsAsync(
        SchemaObjectName table,
        string column,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        return await ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns c
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND t.name = @table AND c.name = @column
            """,
            new { schema = table.Schema ?? "dbo", table = table.Name, column },
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
            SELECT s.name AS SchemaName, o.name AS Name, o.type AS Type,
                   o.is_ms_shipped AS IsSystemObject
            FROM sys.objects o
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V')
              AND (@schema IS NULL OR s.name = @schema)
              AND (@includeSystem = 1 OR o.is_ms_shipped = 0)
            ORDER BY s.name, o.type, o.name
            """,
            new { schema, includeSystem = includeSystemObjects },
            cancellationToken);

        return rows.Select(row => new SchemaObjectInfo(
            new SchemaObjectName(row.Name, row.SchemaName),
            row.Type.Trim().Equals("V", StringComparison.OrdinalIgnoreCase)
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
        var columns = options.IncludeColumns
            ? await ReadColumnsAsync(table, cancellationToken)
            : [];
        var primaryKey = options.IncludePrimaryKeys
            ? await ReadPrimaryKeyAsync(table, cancellationToken)
            : null;
        var foreignKeys = options.IncludeForeignKeys
            ? await ReadForeignKeysAsync(table, cancellationToken)
            : [];
        var indexes = options.IncludeIndexes
            ? await ReadIndexesAsync(table, cancellationToken)
            : [];
        return new TableSchema(
            new SchemaObjectName(table.Name, table.Schema ?? "dbo"),
            options.IncludeColumns ? columns : [],
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

        return new DatabaseSchema(await ScalarAsync<string>("SELECT DB_NAME()", cancellationToken: cancellationToken), tables, views);
    }

    private async Task<IReadOnlyList<ColumnSchema>> ReadColumnsAsync(
        SchemaObjectName item,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<ColumnRow>(
            """
            SELECT c.column_id - 1 AS Ordinal, c.name AS Name, ty.name AS TypeName,
                   COALESCE(base_ty.name, ty.name) AS SystemTypeName,
                   c.max_length AS MaxLength, c.precision AS Precision, c.scale AS Scale,
                   c.is_nullable AS IsNullable, c.is_identity AS IsIdentity,
                   c.is_computed AS IsComputed, ISNULL(cc.is_persisted, 0) AS IsPersisted,
                   dc.definition AS DefaultExpression
            FROM sys.columns c
            INNER JOIN sys.objects o ON o.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.types base_ty
                ON base_ty.user_type_id = c.system_type_id
               AND base_ty.user_type_id = base_ty.system_type_id
            LEFT JOIN sys.computed_columns cc
                ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            WHERE s.name = @schema AND o.name = @name AND o.type IN ('U', 'V')
            ORDER BY c.column_id
            """,
            new { schema = item.Schema ?? "dbo", name = item.Name },
            cancellationToken);

        return rows.Select(row => row.ToSchema()).ToArray();
    }

    private async Task<string?> ReadViewDefinitionAsync(
        SchemaObjectName view,
        CancellationToken cancellationToken) =>
        await ScalarAsync<string?>(
            """
            SELECT m.definition
            FROM sys.sql_modules m
            INNER JOIN sys.views v ON v.object_id = m.object_id
            INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
            WHERE s.name = @schema AND v.name = @name
            """,
            new { schema = view.Schema ?? "dbo", name = view.Name },
            cancellationToken);

    private async Task<PrimaryKeySchema?> ReadPrimaryKeyAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<KeyColumnRow>(
            """
            SELECT kc.name AS ConstraintName, c.name AS ColumnName,
                   ic.key_ordinal AS Ordinal
            FROM sys.key_constraints kc
            INNER JOIN sys.tables t ON t.object_id = kc.parent_object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.index_columns ic
                ON ic.object_id = t.object_id AND ic.index_id = kc.unique_index_id
            INNER JOIN sys.columns c
                ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE kc.type = 'PK' AND s.name = @schema AND t.name = @name
            ORDER BY ic.key_ordinal
            """,
            new { schema = table.Schema ?? "dbo", name = table.Name },
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
            SELECT fk.object_id AS Id, fk.name AS Name,
                   fkc.constraint_column_id AS Ordinal,
                   pc.name AS ColumnName, rs.name AS ReferencedSchema,
                   rt.name AS ReferencedTable, rc.name AS ReferencedColumn,
                   fk.update_referential_action_desc AS OnUpdate,
                   fk.delete_referential_action_desc AS OnDelete
            FROM sys.foreign_keys fk
            INNER JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            INNER JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
            INNER JOIN sys.foreign_key_columns fkc
                ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.columns pc
                ON pc.object_id = fkc.parent_object_id
               AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            INNER JOIN sys.columns rc
                ON rc.object_id = fkc.referenced_object_id
               AND rc.column_id = fkc.referenced_column_id
            WHERE ps.name = @schema AND pt.name = @name
            ORDER BY fk.object_id, fkc.constraint_column_id
            """,
            new { schema = table.Schema ?? "dbo", name = table.Name },
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

    private static ReferentialAction ParseReferentialAction(string value) =>
        value.Trim().Replace("_", "", StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "NOACTION" => ReferentialAction.NoAction,
            "CASCADE" => ReferentialAction.Cascade,
            "SETNULL" => ReferentialAction.SetNull,
            "SETDEFAULT" => ReferentialAction.SetDefault,
            _ => throw new InvalidOperationException(
                $"SQL Server returned unsupported referential action '{value}'.")
        };

    private async Task<IReadOnlyList<IndexSchema>> ReadIndexesAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<IndexRow>(
            """
            SELECT i.index_id AS IndexId, i.name AS IndexName,
                   i.is_unique AS IsUnique, i.is_primary_key AS IsPrimaryKey,
                   i.is_unique_constraint AS IsUniqueConstraint,
                   i.filter_definition AS FilterExpression,
                   ic.index_column_id - 1 AS Ordinal,
                   c.name AS ColumnName, ic.is_descending_key AS IsDescending,
                   ic.is_included_column AS IsIncluded
            FROM sys.indexes i
            INNER JOIN sys.tables t ON t.object_id = i.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.index_columns ic
                ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns c
                ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE s.name = @schema AND t.name = @name
              AND i.index_id > 0 AND i.is_hypothetical = 0
              AND (ic.key_ordinal > 0 OR ic.is_included_column = 1)
            ORDER BY i.index_id, ic.index_column_id
            """,
            new { schema = table.Schema ?? "dbo", name = table.Name },
            cancellationToken);

        return rows
            .GroupBy(row => row.IndexId)
            .Select(group =>
            {
                var first = group.First();
                return new IndexSchema(
                    first.IndexName,
                    group.OrderBy(row => row.Ordinal).Select(row =>
                        new IndexColumnSchema(
                            row.ColumnName,
                            row.Ordinal,
                            row.IsDescending,
                            isIncluded: row.IsIncluded)),
                    first.IsUnique,
                    first.FilterExpression,
                    first.IsPrimaryKey
                        ? SchemaIndexOrigin.PrimaryKey
                        : first.IsUniqueConstraint
                            ? SchemaIndexOrigin.UniqueConstraint
                            : SchemaIndexOrigin.Created);
            })
            .ToArray();
    }

    private sealed class ObjectRow
    {
        public required string SchemaName { get; init; }
        public required string Name { get; init; }
        public required string Type { get; init; }
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
        public int Id { get; init; }
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
        public int IndexId { get; init; }
        public required string IndexName { get; init; }
        public bool IsUnique { get; init; }
        public bool IsPrimaryKey { get; init; }
        public bool IsUniqueConstraint { get; init; }
        public string? FilterExpression { get; init; }
        public int Ordinal { get; init; }
        public required string ColumnName { get; init; }
        public bool IsDescending { get; init; }
        public bool IsIncluded { get; init; }
    }

    private sealed class ColumnRow
    {
        public int Ordinal { get; init; }
        public required string Name { get; init; }
        public required string TypeName { get; init; }
        public required string SystemTypeName { get; init; }
        public short MaxLength { get; init; }
        public byte Precision { get; init; }
        public byte Scale { get; init; }
        public bool IsNullable { get; init; }
        public bool IsIdentity { get; init; }
        public bool IsComputed { get; init; }
        public bool IsPersisted { get; init; }
        public string? DefaultExpression { get; init; }

        public ColumnSchema ToSchema()
        {
            var type = SqlServerTypeMapping.Resolve(SystemTypeName);
            return new ColumnSchema(
                Name,
                Ordinal,
                FormatStoreType(),
                type.DbType,
                type.ClrType,
                IsNullable,
                GetGeneratedKind(),
                DefaultExpression,
                isAutoIncrement: IsIdentity);
        }

        private SchemaGeneratedKind GetGeneratedKind()
        {
            if (SystemTypeName.Equals("timestamp", StringComparison.OrdinalIgnoreCase)
                || SystemTypeName.Equals("rowversion", StringComparison.OrdinalIgnoreCase))
            {
                return SchemaGeneratedKind.RowVersion;
            }

            if (IsIdentity)
            {
                return SchemaGeneratedKind.Identity;
            }

            return IsComputed
                ? IsPersisted
                    ? SchemaGeneratedKind.ComputedStored
                    : SchemaGeneratedKind.ComputedVirtual
                : SchemaGeneratedKind.None;
        }

        private string FormatStoreType() => TypeName.ToLowerInvariant() switch
        {
            "varchar" or "char" or "varbinary" or "binary" =>
                $"{TypeName}({(MaxLength < 0 ? "max" : MaxLength)})",
            "nvarchar" or "nchar" =>
                $"{TypeName}({(MaxLength < 0 ? "max" : MaxLength / 2)})",
            "decimal" or "numeric" => $"{TypeName}({Precision},{Scale})",
            "datetime2" or "datetimeoffset" or "time" => $"{TypeName}({Scale})",
            _ => TypeName
        };
    }
}
