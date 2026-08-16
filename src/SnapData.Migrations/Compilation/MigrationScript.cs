namespace SnapData.Migrations;

public sealed class MigrationScript
{
    private readonly IReadOnlyList<MigrationStatement> statements;

    public MigrationScript(
        string migrationId,
        MigrationDirection direction,
        IEnumerable<MigrationStatement> statements)
    {
        MigrationId = string.IsNullOrWhiteSpace(migrationId)
            ? throw new ArgumentException("A migration ID is required.", nameof(migrationId))
            : migrationId;

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown migration direction.");
        }

        Direction = direction;

        var snapshot = statements?.ToArray()
            ?? throw new ArgumentNullException(nameof(statements));
        if (snapshot.Any(statement => statement is null))
        {
            throw new ArgumentException("Migration statements cannot contain null values.", nameof(statements));
        }

        this.statements = Array.AsReadOnly(snapshot);
    }

    public string MigrationId { get; }

    public MigrationDirection Direction { get; }

    public IReadOnlyList<MigrationStatement> Statements => statements;

    public override string ToString() => string.Join(Environment.NewLine, statements);
}
