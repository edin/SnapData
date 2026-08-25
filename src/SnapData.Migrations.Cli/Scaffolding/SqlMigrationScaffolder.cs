using System.Text;

namespace SnapData.Migrations.Cli.Scaffolding;

internal sealed record SqlMigrationScaffoldResult(
    string Id,
    string UpFile,
    string? DownFile,
    bool Combined);

internal sealed class SqlMigrationScaffolder(
    MigrationIdGenerator? idGenerator = null)
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly MigrationIdGenerator idGenerator = idGenerator ?? new();

    public SqlMigrationScaffoldResult Create(
        string scriptsDirectory,
        string migrationName,
        bool combined = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptsDirectory);
        var directory = Path.GetFullPath(scriptsDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"SQL migration directory '{directory}' does not exist.");
        }

        var name = MigrationName.Parse(migrationName);
        var existingIds = Directory.GetFiles(
                directory,
                "*.sql",
                SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(
                ".down.sql",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileNameWithoutExtension(path)!);
        var id = idGenerator.Generate(name, existingIds);
        var upFile = Path.Combine(directory, $"{id}.sql");
        var downFile = combined
            ? null
            : Path.Combine(directory, $"{id}.down.sql");
        EnsureTargetsAreNew(upFile, downFile);

        var created = new List<string>();
        try
        {
            WriteNew(
                upFile,
                combined
                    ? CombinedTemplate(id, name.ClassName)
                    : DirectionTemplate(id, name.ClassName, "Up"));
            created.Add(upFile);
            if (downFile is not null)
            {
                WriteNew(
                    downFile,
                    DirectionTemplate(id, name.ClassName, "Down"));
                created.Add(downFile);
            }
        }
        catch
        {
            foreach (var path in created)
            {
                File.Delete(path);
            }
            throw;
        }

        return new SqlMigrationScaffoldResult(id, upFile, downFile, combined);
    }

    private static void EnsureTargetsAreNew(string upFile, string? downFile)
    {
        var existing = new[] { upFile, downFile }
            .Where(path => path is not null && File.Exists(path))
            .ToArray();
        if (existing.Length > 0)
        {
            throw new InvalidOperationException(
                "Migration creation stopped because the following path(s) already exist: " +
                string.Join(", ", existing) + ". No files were changed.");
        }
    }

    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var writer = new StreamWriter(stream, Utf8WithoutBom);
        writer.Write(content);
        writer.WriteLine();
    }

    private static string DirectionTemplate(
        string id,
        string className,
        string direction) => $$"""
        -- Migration {{id}}: {{className}} ({{direction}})

        -- Write the {{direction}} SQL here.
        """;

    private static string CombinedTemplate(string id, string className) => $$"""
        -- Migration {{id}}: {{className}}

        -- Write the Up SQL here.

        -- snapdata:down

        -- Write the Down SQL here.
        """;
}
