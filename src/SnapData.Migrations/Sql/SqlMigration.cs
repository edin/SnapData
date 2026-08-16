namespace SnapData.Migrations;

public sealed class SqlMigration : Migration
{
    private readonly IReadOnlyList<string> upStatements;
    private readonly IReadOnlyList<string>? downStatements;

    public SqlMigration(string id, string upSql, string? downSql = null)
        : this(
            id,
            [upSql],
            downSql is null ? null : [downSql])
    {
    }

    public SqlMigration(
        string id,
        IEnumerable<string> upStatements,
        IEnumerable<string>? downStatements = null)
    {
        Id = Required(id, nameof(id));
        this.upStatements = Snapshot(upStatements, nameof(upStatements), required: true)!;
        this.downStatements = Snapshot(
            downStatements,
            nameof(downStatements),
            required: false);
    }

    public override string Id { get; }

    public IReadOnlyList<string> UpStatements => upStatements;

    public IReadOnlyList<string>? DownStatements => downStatements;

    public override void Up(MigrationPlan migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        foreach (var statement in upStatements)
        {
            migration.ExecuteSql(statement);
        }
    }

    public override void Down(MigrationPlan migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (downStatements is null)
        {
            base.Down(migration);
            return;
        }

        foreach (var statement in downStatements)
        {
            migration.ExecuteSql(statement);
        }
    }

    private static IReadOnlyList<string>? Snapshot(
        IEnumerable<string>? statements,
        string parameter,
        bool required)
    {
        if (statements is null)
        {
            if (required)
            {
                throw new ArgumentNullException(parameter);
            }

            return null;
        }

        var snapshot = statements.Select(statement => Required(statement, parameter)).ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "At least one SQL statement is required.",
                parameter);
        }

        return Array.AsReadOnly(snapshot);
    }

    private static string Required(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameter)
            : value;
}
