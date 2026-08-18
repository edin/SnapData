namespace SnapData.Migrations;

internal static class MigrationIndexName
{
    public static string Get(string table, IndexDefinition index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(index);
        return index.Name ??
            $"{(index.IsUnique ? "UX" : "IX")}_{table.Split('.').Last()}_" +
            string.Join("_", index.Columns.Select(column => column.Name));
    }
}
