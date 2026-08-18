namespace SnapData.Migrations;

internal static class MigrationOperationConditions
{
    public static MigrationStatement Apply(
        MigrationOperation operation,
        MigrationStatement statement)
    {
        if (operation.Condition == MigrationOperationCondition.None)
        {
            return statement;
        }

        var description = operation switch
        {
            DropTableOperation drop when
                operation.Condition == MigrationOperationCondition.IfExists =>
                $"IF TABLE EXISTS {Name(drop.Table)}",
            AddColumnOperation add when
                operation.Condition == MigrationOperationCondition.IfNotExists =>
                $"IF COLUMN NOT EXISTS {Name(add.Table)}.{Name(add.Column.Name)}",
            DropColumnOperation drop when
                operation.Condition == MigrationOperationCondition.IfExists =>
                $"IF COLUMN EXISTS {Name(drop.Table)}.{Name(drop.Column)}",
            CreateIndexOperation create when
                operation.Condition == MigrationOperationCondition.IfNotExists =>
                $"IF INDEX NOT EXISTS {Name(create.Table)}.{Name(MigrationIndexName.Get(create.Table, create.Index))}",
            DropIndexOperation drop when
                operation.Condition == MigrationOperationCondition.IfExists =>
                $"IF INDEX EXISTS {Name(drop.Table)}.{Name(drop.Index)}",
            _ => throw new InvalidOperationException(
                $"Condition '{operation.Condition}' is not valid for " +
                $"'{operation.GetType().Name}'.")
        };
        return new MigrationStatement(
            $"/* SnapData: {description} */{Environment.NewLine}{statement.Sql}");
    }

    private static string Name(string value) =>
        value.Replace("*/", "* /", StringComparison.Ordinal);

}
