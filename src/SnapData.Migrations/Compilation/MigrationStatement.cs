namespace SnapData.Migrations;

public sealed record MigrationStatement
{
    public MigrationStatement(string sql)
    {
        Sql = string.IsNullOrWhiteSpace(sql)
            ? throw new ArgumentException("A non-empty SQL statement is required.", nameof(sql))
            : sql;
    }

    public string Sql { get; }

    public override string ToString() => Sql;
}
