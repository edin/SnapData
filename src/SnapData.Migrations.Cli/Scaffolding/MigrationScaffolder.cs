using System.Security;
using System.Text;

namespace SnapData.Migrations.Cli.Scaffolding;

internal enum MigrationScaffoldKind
{
    CSharp,
    Sql
}

internal sealed record MigrationScaffoldResult(
    string Directory,
    string ConfigFile,
    string SourceDirectory,
    string? ProjectFile,
    MigrationScaffoldKind Kind,
    IReadOnlyList<string> MigrationFiles);

internal sealed class MigrationScaffolder
{
    private const string MigrationsDirectoryName = "Migrations";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public MigrationScaffoldResult Create(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        var target = Path.GetFullPath(targetDirectory);
        var configFile = Path.Combine(target, "snap.ini");
        var migrationsDirectory = Path.Combine(target, MigrationsDirectoryName);
        if (File.Exists(configFile) || Directory.Exists(migrationsDirectory))
        {
            var existing = new List<string>();
            if (File.Exists(configFile))
            {
                existing.Add(configFile);
            }
            if (Directory.Exists(migrationsDirectory))
            {
                existing.Add(migrationsDirectory);
            }
            throw new InvalidOperationException(
                "Initialization stopped because the following path(s) already exist: " +
                string.Join(", ", existing) + ". No files were changed.");
        }

        var projectFile = Path.Combine(migrationsDirectory, "App.Migrations.csproj");
        var bundleFile = Path.Combine(migrationsDirectory, "AppMigrationBundle.cs");
        var createUsersFile = Path.Combine(migrationsDirectory, "CreateUsers.cs");
        var addUserEmailFile = Path.Combine(migrationsDirectory, "AddUserEmail.cs");
        var ignoreFile = Path.Combine(migrationsDirectory, ".gitignore");
        var projectReference = FindSourceProject(target);

        Directory.CreateDirectory(target);
        Directory.CreateDirectory(migrationsDirectory);
        WriteNew(configFile, ConfigTemplate);
        WriteNew(projectFile, ProjectTemplate(projectFile, projectReference));
        WriteNew(bundleFile, BundleTemplate);
        WriteNew(createUsersFile, CreateUsersTemplate);
        WriteNew(addUserEmailFile, AddUserEmailTemplate);
        WriteNew(ignoreFile, DatabaseIgnoreTemplate);

        return new MigrationScaffoldResult(
            target,
            configFile,
            migrationsDirectory,
            projectFile,
            MigrationScaffoldKind.CSharp,
            [createUsersFile, addUserEmailFile]);
    }

    public MigrationScaffoldResult CreateSql(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        var target = Path.GetFullPath(targetDirectory);
        var configFile = Path.Combine(target, "snap.ini");
        var scriptsDirectory = Path.Combine(target, "Scripts");
        EnsureNewWorkspace(configFile, scriptsDirectory);

        var createUsersFile = Path.Combine(scriptsDirectory, "001_create_users.sql");
        var createUsersDownFile = Path.Combine(
            scriptsDirectory, "001_create_users.down.sql");
        var addUserEmailFile = Path.Combine(scriptsDirectory, "002_add_user_email.sql");
        var addUserEmailDownFile = Path.Combine(
            scriptsDirectory, "002_add_user_email.down.sql");
        var ignoreFile = Path.Combine(scriptsDirectory, ".gitignore");

        Directory.CreateDirectory(target);
        Directory.CreateDirectory(scriptsDirectory);
        WriteNew(configFile, SqlConfigTemplate);
        WriteNew(createUsersFile, SqlCreateUsersTemplate);
        WriteNew(createUsersDownFile, SqlDropUsersTemplate);
        WriteNew(addUserEmailFile, SqlAddUserEmailTemplate);
        WriteNew(addUserEmailDownFile, SqlDropUserEmailTemplate);
        WriteNew(ignoreFile, DatabaseIgnoreTemplate);

        return new MigrationScaffoldResult(
            target,
            configFile,
            scriptsDirectory,
            ProjectFile: null,
            MigrationScaffoldKind.Sql,
            [createUsersFile, addUserEmailFile]);
    }

    private static void EnsureNewWorkspace(string configFile, string sourceDirectory)
    {
        if (!File.Exists(configFile) && !Directory.Exists(sourceDirectory))
        {
            return;
        }

        var existing = new List<string>();
        if (File.Exists(configFile))
        {
            existing.Add(configFile);
        }
        if (Directory.Exists(sourceDirectory))
        {
            existing.Add(sourceDirectory);
        }
        throw new InvalidOperationException(
            "Initialization stopped because the following path(s) already exist: " +
            string.Join(", ", existing) + ". No files were changed.");
    }

    private static string? FindSourceProject(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "SnapData.Migrations",
                "SnapData.Migrations.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static string ProjectTemplate(string projectFile, string? sourceProject)
    {
        var reference = sourceProject is null
            ? $"<PackageReference Include=\"SnapData.Migrations\" Version=\"{CliVersion.Value}\" />"
            : $"<ProjectReference Include=\"{SecurityElement.Escape(Path.GetRelativePath(
                Path.GetDirectoryName(projectFile)!, sourceProject))}\" />";
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>

              <ItemGroup>
                {{reference}}
              </ItemGroup>

            </Project>
            """;
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

    private const string ConfigTemplate = """
        [Migration]
        Provider=Sqlite
        Connection=Data Source=Migrations/app.db
        Project=Migrations/App.Migrations.csproj
        TargetFramework=net8.0
        Bundle=App.Migrations.AppMigrationBundle
        Locking=Required
        """;

    private const string SqlConfigTemplate = """
        [Migration]
        Provider=Sqlite
        Connection=Data Source=Scripts/app.db
        Scripts=Scripts
        Locking=Required
        """;

    private const string SqlCreateUsersTemplate = """
        CREATE TABLE users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            active INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        """;

    private const string SqlDropUsersTemplate = "DROP TABLE users;";

    private const string SqlAddUserEmailTemplate = """
        ALTER TABLE users ADD COLUMN email TEXT;
        CREATE UNIQUE INDEX UX_users_email ON users (email);
        """;

    private const string SqlDropUserEmailTemplate = """
        DROP INDEX UX_users_email;
        ALTER TABLE users DROP COLUMN email;
        """;

    private const string BundleTemplate = """
        using SnapData.Migrations;

        namespace App.Migrations;

        public sealed class AppMigrationBundle : MigrationBundle
        {
            protected override void Configure(MigrationCollection migrations)
            {
                migrations
                    .Add<CreateUsers>()
                    .Add<AddUserEmail>();
            }
        }
        """;

    private const string CreateUsersTemplate = """
        using SnapData.Migrations;

        namespace App.Migrations;

        public sealed class CreateUsers : Migration
        {
            public override string Id => "001_create_users";

            public override void Up(MigrationPlan migration)
            {
                using var table = migration.CreateTable("users");
                table.Identity();
                table.String("name", 150);
                table.Boolean("active").Default(true);
                table.Timestamps();
            }

            public override void Down(MigrationPlan migration) =>
                migration.DropTable("users");
        }
        """;

    private const string AddUserEmailTemplate = """
        using SnapData.Migrations;

        namespace App.Migrations;

        public sealed class AddUserEmail : Migration
        {
            public override string Id => "002_add_user_email";

            public override void Up(MigrationPlan migration)
            {
                using var table = migration.AlterTable("users");
                table.String("email", 250).Nullable();
                table.CreateUniqueIndex("UX_users_email", "email");
            }

            public override void Down(MigrationPlan migration)
            {
                using var table = migration.AlterTable("users");
                table.DropIndex("UX_users_email");
                table.DropColumn("email");
            }
        }
        """;

    private const string DatabaseIgnoreTemplate = """
        app.db
        app.db-shm
        app.db-wal
        """;
}
