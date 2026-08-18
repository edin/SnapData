namespace SnapData.Migrations;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

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
        Fingerprint = CalculateFingerprint(snapshot);
    }

    public string MigrationId { get; }

    public MigrationDirection Direction { get; }

    public IReadOnlyList<MigrationStatement> Statements => statements;

    public string Fingerprint { get; }

    public override string ToString() => string.Join(Environment.NewLine, statements);

    private static string CalculateFingerprint(
        IReadOnlyCollection<MigrationStatement> statements)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendInt32(hash, statements.Count);
        foreach (var statement in statements)
        {
            var normalized = statement.Sql
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            var bytes = Encoding.UTF8.GetBytes(normalized);
            AppendInt32(hash, bytes.Length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
