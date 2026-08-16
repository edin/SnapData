namespace SnapData.Schema;

public sealed class FirebirdSchemaInspector : SchemaInspector
{
    public FirebirdSchemaInspector(SnapDatabase database) : base(database)
    {
    }

    public FirebirdSchemaInspector(IDbExecutor executor) : base(executor)
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
            FROM RDB$RELATIONS r
            WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
              AND r.RDB$VIEW_BLR IS NULL
              AND (TRIM(r.RDB$RELATION_NAME) = @name
                   OR TRIM(r.RDB$RELATION_NAME) = UPPER(@name))
            """,
            new { name = table.Name },
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
            FROM RDB$RELATION_FIELDS rf
            JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
            WHERE r.RDB$VIEW_BLR IS NULL
              AND (TRIM(rf.RDB$RELATION_NAME) = @table
                   OR TRIM(rf.RDB$RELATION_NAME) = UPPER(@table))
              AND (TRIM(rf.RDB$FIELD_NAME) = @column
                   OR TRIM(rf.RDB$FIELD_NAME) = UPPER(@column))
            """,
            new { table = table.Name, column },
            cancellationToken) > 0;
    }

    public override async Task<IReadOnlyList<SchemaObjectInfo>> GetObjectsAsync(
        string? schema = null,
        bool includeSystemObjects = false,
        CancellationToken cancellationToken = default)
    {
        if (schema is not null)
        {
            throw new ArgumentException("Firebird does not support relation schemas.", nameof(schema));
        }

        var rows = await QueryAsync<ObjectRow>(
            """
            SELECT TRIM(r.RDB$RELATION_NAME) AS Name,
                   CASE WHEN r.RDB$VIEW_BLR IS NULL THEN 0 ELSE 1 END AS IsView,
                   COALESCE(r.RDB$SYSTEM_FLAG, 0) AS IsSystemObject
            FROM RDB$RELATIONS r
            WHERE (@includeSystem = 1 OR COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0)
            ORDER BY CASE WHEN r.RDB$VIEW_BLR IS NULL THEN 0 ELSE 1 END,
                     r.RDB$RELATION_NAME
            """,
            new { includeSystem = includeSystemObjects },
            cancellationToken);

        return rows.Select(row => new SchemaObjectInfo(
            new SchemaObjectName(row.Name.Trim()),
            row.IsView == 0 ? SchemaObjectKind.Table : SchemaObjectKind.View,
            row.IsSystemObject != 0)).ToArray();
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
        return new TableSchema(
            new SchemaObjectName(table.Name),
            options.IncludeColumns ? await ReadColumnsAsync(table, cancellationToken) : [],
            options.IncludePrimaryKeys
                ? await ReadPrimaryKeyAsync(table, cancellationToken)
                : null,
            options.IncludeForeignKeys
                ? await ReadForeignKeysAsync(table, cancellationToken)
                : [],
            options.IncludeIndexes
                ? await ReadIndexesAsync(table, cancellationToken)
                : [],
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
            await ScalarAsync<string>(
                "SELECT RDB$GET_CONTEXT('SYSTEM', 'DB_NAME') FROM RDB$DATABASE",
                cancellationToken: cancellationToken),
            tables,
            views);
    }

    private async Task<IReadOnlyList<ColumnSchema>> ReadColumnsAsync(
        SchemaObjectName item,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<ColumnRow>(
            """
            SELECT rf.RDB$FIELD_POSITION AS Ordinal,
                   TRIM(rf.RDB$FIELD_NAME) AS Name,
                   f.RDB$FIELD_TYPE AS FieldType,
                   COALESCE(f.RDB$FIELD_SUB_TYPE, 0) AS FieldSubType,
                   f.RDB$FIELD_LENGTH AS FieldLength,
                   f.RDB$CHARACTER_LENGTH AS CharacterLength,
                   f.RDB$FIELD_PRECISION AS FieldPrecision,
                   COALESCE(f.RDB$FIELD_SCALE, 0) AS FieldScale,
                   CASE WHEN COALESCE(rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, 0) = 1
                       THEN 0 ELSE 1 END AS IsNullable,
                   COALESCE(rf.RDB$DEFAULT_SOURCE, f.RDB$DEFAULT_SOURCE) AS DefaultExpression,
                   f.RDB$COMPUTED_SOURCE AS ComputedExpression,
                   rf.RDB$IDENTITY_TYPE AS IdentityType,
                   TRIM(cs.RDB$CHARACTER_SET_NAME) AS CharacterSet,
                   TRIM(rf.RDB$FIELD_SOURCE) AS FieldSource,
                   COALESCE(f.RDB$DIMENSIONS, 0) AS Dimensions
            FROM RDB$RELATION_FIELDS rf
            JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
            LEFT JOIN RDB$CHARACTER_SETS cs
                ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID
            WHERE TRIM(rf.RDB$RELATION_NAME) = @name
               OR TRIM(rf.RDB$RELATION_NAME) = UPPER(@name)
            ORDER BY rf.RDB$FIELD_POSITION
            """,
            new { name = item.Name },
            cancellationToken);

        return rows.Select(row => row.ToSchema()).ToArray();
    }

    private async Task<string?> ReadViewDefinitionAsync(
        SchemaObjectName view,
        CancellationToken cancellationToken) =>
        await ScalarAsync<string?>(
            """
            SELECT r.RDB$VIEW_SOURCE
            FROM RDB$RELATIONS r
            WHERE r.RDB$VIEW_BLR IS NOT NULL
              AND (TRIM(r.RDB$RELATION_NAME) = @name
                   OR TRIM(r.RDB$RELATION_NAME) = UPPER(@name))
            """,
            new { name = view.Name },
            cancellationToken);

    private async Task<PrimaryKeySchema?> ReadPrimaryKeyAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<KeyColumnRow>(
            """
            SELECT TRIM(rc.RDB$CONSTRAINT_NAME) AS ConstraintName,
                   TRIM(seg.RDB$FIELD_NAME) AS ColumnName,
                   seg.RDB$FIELD_POSITION AS Ordinal
            FROM RDB$RELATION_CONSTRAINTS rc
            JOIN RDB$INDEX_SEGMENTS seg
                ON seg.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
            WHERE rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND (TRIM(rc.RDB$RELATION_NAME) = @name
                   OR TRIM(rc.RDB$RELATION_NAME) = UPPER(@name))
            ORDER BY seg.RDB$FIELD_POSITION
            """,
            new { name = table.Name },
            cancellationToken);

        return rows.Count == 0
            ? null
            : new PrimaryKeySchema(
                rows[0].ConstraintName.Trim(),
                rows.OrderBy(row => row.Ordinal).Select(row => row.ColumnName.Trim()));
    }

    private async Task<IReadOnlyList<ForeignKeySchema>> ReadForeignKeysAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<ForeignKeyRow>(
            """
            SELECT TRIM(fk.RDB$CONSTRAINT_NAME) AS Name,
                   local_seg.RDB$FIELD_POSITION AS Ordinal,
                   TRIM(local_seg.RDB$FIELD_NAME) AS ColumnName,
                   TRIM(pk.RDB$RELATION_NAME) AS ReferencedTable,
                   TRIM(referenced_seg.RDB$FIELD_NAME) AS ReferencedColumn,
                   TRIM(ref.RDB$UPDATE_RULE) AS OnUpdate,
                   TRIM(ref.RDB$DELETE_RULE) AS OnDelete
            FROM RDB$RELATION_CONSTRAINTS fk
            JOIN RDB$REF_CONSTRAINTS ref
                ON ref.RDB$CONSTRAINT_NAME = fk.RDB$CONSTRAINT_NAME
            JOIN RDB$RELATION_CONSTRAINTS pk
                ON pk.RDB$CONSTRAINT_NAME = ref.RDB$CONST_NAME_UQ
            JOIN RDB$INDEX_SEGMENTS local_seg
                ON local_seg.RDB$INDEX_NAME = fk.RDB$INDEX_NAME
            JOIN RDB$INDEX_SEGMENTS referenced_seg
                ON referenced_seg.RDB$INDEX_NAME = pk.RDB$INDEX_NAME
               AND referenced_seg.RDB$FIELD_POSITION = local_seg.RDB$FIELD_POSITION
            WHERE fk.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
              AND (TRIM(fk.RDB$RELATION_NAME) = @name
                   OR TRIM(fk.RDB$RELATION_NAME) = UPPER(@name))
            ORDER BY fk.RDB$CONSTRAINT_NAME, local_seg.RDB$FIELD_POSITION
            """,
            new { name = table.Name },
            cancellationToken);

        return rows
            .GroupBy(row => row.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderBy(row => row.Ordinal).ToArray();
                var first = ordered[0];
                return new ForeignKeySchema(
                    first.Name.Trim(),
                    ordered.Select(row => row.ColumnName.Trim()),
                    new SchemaObjectName(first.ReferencedTable.Trim()),
                    ordered.Select(row => row.ReferencedColumn.Trim()),
                    ParseReferentialAction(first.OnUpdate),
                    ParseReferentialAction(first.OnDelete));
            })
            .ToArray();
    }

    private static ReferentialAction ParseReferentialAction(string value) =>
        value.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "NOACTION" => ReferentialAction.NoAction,
            "RESTRICT" => ReferentialAction.Restrict,
            "CASCADE" => ReferentialAction.Cascade,
            "SETNULL" => ReferentialAction.SetNull,
            "SETDEFAULT" => ReferentialAction.SetDefault,
            _ => throw new InvalidOperationException(
                $"Firebird returned unsupported referential action '{value}'.")
        };

    private async Task<IReadOnlyList<IndexSchema>> ReadIndexesAsync(
        SchemaObjectName table,
        CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<IndexRow>(
            """
            SELECT TRIM(i.RDB$INDEX_NAME) AS Name,
                   COALESCE(i.RDB$UNIQUE_FLAG, 0) AS IsUnique,
                   COALESCE(i.RDB$INDEX_TYPE, 0) AS IsDescending,
                   CASE WHEN COALESCE(i.RDB$INDEX_INACTIVE, 0) = 0
                       THEN 1 ELSE 0 END AS IsVisible,
                   i.RDB$EXPRESSION_SOURCE AS Expression,
                   seg.RDB$FIELD_POSITION AS Ordinal,
                   TRIM(seg.RDB$FIELD_NAME) AS ColumnName,
                   TRIM(rc.RDB$CONSTRAINT_TYPE) AS ConstraintType
            FROM RDB$INDICES i
            LEFT JOIN RDB$INDEX_SEGMENTS seg
                ON seg.RDB$INDEX_NAME = i.RDB$INDEX_NAME
            LEFT JOIN RDB$RELATION_CONSTRAINTS rc
                ON rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME
            WHERE TRIM(i.RDB$RELATION_NAME) = @name
               OR TRIM(i.RDB$RELATION_NAME) = UPPER(@name)
            ORDER BY i.RDB$INDEX_NAME, seg.RDB$FIELD_POSITION
            """,
            new { name = table.Name },
            cancellationToken);

        return rows
            .GroupBy(row => row.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var expression = first.Expression?.Trim();
                IEnumerable<IndexColumnSchema> columns = expression is not null
                    ? [new IndexColumnSchema(null, 0, first.IsDescending != 0, expression)]
                    : group
                        .OrderBy(row => row.Ordinal)
                        .Select(row => new IndexColumnSchema(
                            row.ColumnName!.Trim(),
                            row.Ordinal!.Value,
                            first.IsDescending != 0));
                return new IndexSchema(
                    first.Name.Trim(),
                    columns,
                    first.IsUnique != 0,
                    origin: ParseIndexOrigin(first.ConstraintType),
                    isVisible: first.IsVisible != 0,
                    method: "BTREE");
            })
            .ToArray();
    }

    private static SchemaIndexOrigin ParseIndexOrigin(string? constraintType) =>
        constraintType?.Trim().ToUpperInvariant() switch
        {
            "PRIMARY KEY" => SchemaIndexOrigin.PrimaryKey,
            "UNIQUE" => SchemaIndexOrigin.UniqueConstraint,
            _ => SchemaIndexOrigin.Created
        };

    private sealed class ObjectRow
    {
        public required string Name { get; init; }
        public int IsView { get; init; }
        public int IsSystemObject { get; init; }
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
        public required string ReferencedTable { get; init; }
        public required string ReferencedColumn { get; init; }
        public required string OnUpdate { get; init; }
        public required string OnDelete { get; init; }
    }

    private sealed class IndexRow
    {
        public required string Name { get; init; }
        public int IsUnique { get; init; }
        public int IsDescending { get; init; }
        public int IsVisible { get; init; }
        public string? Expression { get; init; }
        public int? Ordinal { get; init; }
        public string? ColumnName { get; init; }
        public string? ConstraintType { get; init; }
    }

    private sealed class ColumnRow
    {
        public int Ordinal { get; init; }
        public required string Name { get; init; }
        public int FieldType { get; init; }
        public int FieldSubType { get; init; }
        public int FieldLength { get; init; }
        public int? CharacterLength { get; init; }
        public int? FieldPrecision { get; init; }
        public int FieldScale { get; init; }
        public int IsNullable { get; init; }
        public string? DefaultExpression { get; init; }
        public string? ComputedExpression { get; init; }
        public int? IdentityType { get; init; }
        public string? CharacterSet { get; init; }
        public required string FieldSource { get; init; }
        public int Dimensions { get; init; }

        public ColumnSchema ToSchema()
        {
            var type = FirebirdTypeMapping.Resolve(
                FieldType,
                FieldSubType,
                FieldLength,
                CharacterLength,
                FieldPrecision,
                FieldScale,
                CharacterSet);
            var storeType = FieldSource.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase)
                ? type.StoreType
                : FieldSource.Trim();
            if (Dimensions > 0)
            {
                storeType += " ARRAY";
                type = new FirebirdTypeInfo(
                    storeType,
                    System.Data.DbType.Object,
                    typeof(Array));
            }
            var isIdentity = IdentityType is not null;
            return new ColumnSchema(
                Name.Trim(),
                Ordinal,
                storeType,
                type.DbType,
                type.ClrType,
                IsNullable != 0,
                isIdentity
                    ? SchemaGeneratedKind.Identity
                    : ComputedExpression is not null
                        ? SchemaGeneratedKind.ComputedVirtual
                        : SchemaGeneratedKind.None,
                NormalizeDefault(DefaultExpression),
                isAutoIncrement: isIdentity);
        }

        private static string? NormalizeDefault(string? value)
        {
            var trimmed = value?.Trim();
            return trimmed?.StartsWith("DEFAULT ", StringComparison.OrdinalIgnoreCase) == true
                ? trimmed[8..].Trim()
                : trimmed;
        }
    }
}
