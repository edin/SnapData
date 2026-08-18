using SnapData.Schema;

namespace SnapData.Migrations;

internal sealed class ConditionalOperationResolver(ISchemaInspector inspector)
{
    private static readonly SchemaReadOptions ConditionalOptions = new()
    {
        IncludeColumns = true,
        IncludePrimaryKeys = false,
        IncludeForeignKeys = false,
        IncludeIndexes = true,
        IncludeViews = false,
        IncludeDefinitionSql = false
    };

    private readonly Dictionary<string, TableState?> tables =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task PreloadAsync(
        IEnumerable<MigrationOperation> operations,
        CancellationToken cancellationToken)
    {
        var names = operations
            .Where(operation => operation.Condition != MigrationOperationCondition.None)
            .Select(TableName)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            await LoadAsync(name, cancellationToken);
        }
    }

    public async Task<bool> ShouldExecuteAsync(
        MigrationOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Condition == MigrationOperationCondition.None)
        {
            return true;
        }

        var table = TableName(operation);
        if (!tables.ContainsKey(table))
        {
            await LoadAsync(table, cancellationToken);
        }
        var state = tables[table];
        return operation switch
        {
            DropTableOperation when
                operation.Condition == MigrationOperationCondition.IfExists =>
                state is not null,
            AddColumnOperation add when
                operation.Condition == MigrationOperationCondition.IfNotExists =>
                state is null || !state.Columns.Contains(add.Column.Name),
            DropColumnOperation drop when
                operation.Condition == MigrationOperationCondition.IfExists =>
                state is not null && state.Columns.Contains(drop.Column),
            CreateIndexOperation create when
                operation.Condition == MigrationOperationCondition.IfNotExists =>
                state is null || !state.Indexes.Contains(
                    MigrationIndexName.Get(create.Table, create.Index)),
            DropIndexOperation drop when
                operation.Condition == MigrationOperationCondition.IfExists =>
                state is not null && state.Indexes.Contains(drop.Index),
            _ => throw new InvalidOperationException(
                $"Condition '{operation.Condition}' is not valid for " +
                $"'{operation.GetType().Name}'.")
        };
    }

    public void RecordExecuted(MigrationOperation operation)
    {
        switch (operation)
        {
            case ExecuteSqlOperation:
                tables.Clear();
                break;
            case CreateTableOperation create when create.IfNotExists:
                tables.Remove(create.Table);
                break;
            case CreateTableOperation create:
                tables[create.Table] = new TableState(
                    create.Columns.Select(column => column.Name),
                    create.Indexes.Select(index =>
                        MigrationIndexName.Get(create.Table, index)));
                break;
            case DropTableOperation drop:
                tables[drop.Table] = null;
                break;
            case RenameTableOperation rename:
                if (tables.Remove(rename.Table, out var renamed))
                {
                    tables[rename.NewName] = renamed;
                }
                else
                {
                    tables.Remove(rename.NewName);
                }
                break;
            case AddColumnOperation add:
                GetOrCreate(add.Table).Columns.Add(add.Column.Name);
                break;
            case DropColumnOperation drop:
                if (tables.TryGetValue(drop.Table, out var droppedTable))
                {
                    droppedTable?.Columns.Remove(drop.Column);
                }
                break;
            case RenameColumnOperation rename:
                if (tables.TryGetValue(rename.Table, out var renamedTable) &&
                    renamedTable is not null)
                {
                    renamedTable.Columns.Remove(rename.Column);
                    renamedTable.Columns.Add(rename.NewName);
                }
                break;
            case CreateIndexOperation create:
                GetOrCreate(create.Table).Indexes.Add(
                    MigrationIndexName.Get(create.Table, create.Index));
                break;
            case DropIndexOperation drop:
                if (tables.TryGetValue(drop.Table, out var indexedTable))
                {
                    indexedTable?.Indexes.Remove(drop.Index);
                }
                break;
        }
    }

    private TableState GetOrCreate(string table)
    {
        if (!tables.TryGetValue(table, out var state) || state is null)
        {
            state = new TableState([], []);
            tables[table] = state;
        }
        return state;
    }

    private async Task LoadAsync(string table, CancellationToken cancellationToken)
    {
        var schema = await inspector.GetTableAsync(
            Parse(table), ConditionalOptions, cancellationToken);
        tables[table] = schema is null
            ? null
            : new TableState(
                schema.Columns.Select(column => column.Name),
                schema.Indexes.Select(index => index.Name));
    }

    private static string TableName(MigrationOperation operation) => operation switch
    {
        DropTableOperation drop => drop.Table,
        AddColumnOperation add => add.Table,
        DropColumnOperation drop => drop.Table,
        CreateIndexOperation create => create.Table,
        DropIndexOperation drop => drop.Table,
        _ => throw new InvalidOperationException(
            $"'{operation.GetType().Name}' does not expose a conditional table.")
    };

    private static SchemaObjectName Parse(string table)
    {
        var parts = table.Split('.', StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 when parts[0].Length > 0 => new SchemaObjectName(parts[0]),
            2 when parts.All(part => part.Length > 0) =>
                new SchemaObjectName(parts[1], parts[0]),
            _ => throw new ArgumentException(
                "A table name must use 'table' or 'schema.table' form.", nameof(table))
        };
    }


    private sealed class TableState(
        IEnumerable<string> columns,
        IEnumerable<string> indexes)
    {
        public HashSet<string> Columns { get; } =
            new(columns, StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Indexes { get; } =
            new(indexes, StringComparer.OrdinalIgnoreCase);
    }
}
