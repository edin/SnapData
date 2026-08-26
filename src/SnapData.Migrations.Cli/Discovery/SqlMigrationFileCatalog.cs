using System.Text.RegularExpressions;
using SnapData.Migrations;

namespace SnapData.Migrations.Cli.Discovery;

internal sealed partial class SqlMigrationFileCatalog
{
    private const string DownSuffix = ".down.sql";

    public async Task<MigrationCatalog> LoadAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"SQL migration directory '{directory}' does not exist.");
        }

        var files = Directory.GetFiles(directory, "*.sql", SearchOption.TopDirectoryOnly);
        var upFiles = files
            .Where(path => !path.EndsWith(DownSuffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        var downFiles = files
            .Where(path => path.EndsWith(DownSuffix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                path => Path.GetFileName(path)[..^DownSuffix.Length],
                StringComparer.OrdinalIgnoreCase);
        ValidateDownFilesHaveUpFiles(upFiles, downFiles);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var migrations = new List<Migration>(upFiles.Length);

        foreach (var upFile in upFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileNameWithoutExtension(upFile);
            if (!ids.Add(id))
            {
                throw new InvalidOperationException(
                    $"SQL migration ID '{id}' is duplicated in '{directory}'.");
            }

            var content = await File.ReadAllTextAsync(upFile, cancellationToken)
                .ConfigureAwait(false);
            var sections = SplitSections(content, upFile);
            string? downSql = null;
            if (downFiles.TryGetValue(id, out var downFile))
            {
                if (sections.DownSql is not null)
                {
                    throw new InvalidOperationException(
                        $"SQL migration '{id}' defines Down SQL both inline and in '{Path.GetFileName(downFile)}'.");
                }
                downSql = RequiredSql(
                    await File.ReadAllTextAsync(downFile, cancellationToken)
                        .ConfigureAwait(false),
                    downFile,
                    "Down");
            }
            else
            {
                downSql = sections.DownSql;
            }

            migrations.Add(new SqlMigration(id, sections.UpSql, downSql));
        }

        return new MigrationCatalog(
            migrations,
            bundleType: null,
            migration => $"{migration.Id}.sql");
    }

    private static void ValidateDownFilesHaveUpFiles(
        IReadOnlyList<string> upFiles,
        IReadOnlyDictionary<string, string> downFiles)
    {
        var upIds = upFiles
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, downFile) in downFiles)
        {
            if (!upIds.Contains(id))
            {
                throw new InvalidOperationException(
                    $"Down migration '{Path.GetFileName(downFile)}' has no matching Up migration '{id}.sql'.");
            }
        }
    }

    private static SqlSections SplitSections(string content, string path)
    {
        var markers = DownMarker().Matches(content);
        if (markers.Count > 1)
        {
            throw new InvalidOperationException(
                $"SQL migration '{Path.GetFileName(path)}' contains more than one '-- snapdata:down' marker.");
        }
        if (markers.Count == 0)
        {
            return new SqlSections(RequiredSql(content, path, "Up"), null);
        }

        var marker = markers[0];
        return new SqlSections(
            RequiredSql(content[..marker.Index], path, "Up"),
            RequiredSql(content[(marker.Index + marker.Length)..], path, "Down"));
    }

    private static string RequiredSql(string content, string path, string direction)
    {
        content = content.Trim();
        return content.Length == 0
            ? throw new InvalidOperationException(
                $"SQL migration '{Path.GetFileName(path)}' has an empty {direction} section.")
            : content;
    }

    [GeneratedRegex(
        @"^[\t ]*--[\t ]*snapdata:down[\t ]*\r?$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DownMarker();

    private sealed record SqlSections(string UpSql, string? DownSql);
}
