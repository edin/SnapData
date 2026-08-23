using System.Globalization;

namespace SnapData.Migrations.Cli.Scaffolding;

internal sealed class MigrationIdGenerator(Func<DateTimeOffset>? utcNow = null)
{
    private const string TimestampFormat = "yyyyMMddHHmmss";
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public string Generate(MigrationName name, IEnumerable<string> existingIds)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(existingIds);

        var ids = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var timestamp = utcNow().UtcDateTime;
        var newestTimestamp = ids
            .Select(TryReadTimestamp)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        if (timestamp <= newestTimestamp)
        {
            timestamp = newestTimestamp.AddSeconds(1);
        }

        while (true)
        {
            var id = $"{timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{name.Slug}";
            if (!ids.Contains(id))
            {
                return id;
            }
            timestamp = timestamp.AddSeconds(1);
        }
    }

    private static DateTime? TryReadTimestamp(string id)
    {
        if (id.Length <= TimestampFormat.Length || id[TimestampFormat.Length] != '_')
        {
            return null;
        }
        return DateTime.TryParseExact(
            id.AsSpan(0, TimestampFormat.Length),
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp)
            ? timestamp
            : null;
    }
}
