using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SnapData.Migrations;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Scaffolding;

namespace SnapData.Migrations.Cli.Tests;

public sealed class CSharpMigrationScaffolderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(
        2026, 8, 26, 16, 45, 10, TimeSpan.Zero);
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"snapdata-add-csharp-{Guid.NewGuid():N}");
    private readonly string projectFile;
    private readonly string bundleFile;

    public CSharpMigrationScaffolderTests()
    {
        Directory.CreateDirectory(directory);
        projectFile = Path.Combine(directory, "App.Migrations.csproj");
        bundleFile = Path.Combine(directory, "AppMigrationBundle.cs");
        File.WriteAllText(projectFile, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    }

    [Fact]
    public void Creates_migration_and_registers_it_without_reformatting_bundle()
    {
        var original = """
            using SnapData.Migrations;

            namespace App.Migrations;

            // Keep this comment and formatting.
            public sealed class AppMigrationBundle : MigrationBundle
            {
                protected override void Configure(MigrationCollection migrations)
                {
                    migrations.Add<Existing>(); // existing registration
                }
            }

            public sealed class Existing : Migration
            {
                public override string Id => "20260826164500_existing";
            }
            """;
        File.WriteAllText(bundleFile, original);

        var result = Scaffolder().Create(Configuration(), "Create orders");

        Assert.Equal("20260826164510_create_orders", result.Id);
        Assert.True(File.Exists(result.MigrationFile));
        var migration = File.ReadAllText(result.MigrationFile);
        Assert.Contains("namespace App.Migrations;", migration);
        Assert.Contains("class CreateOrders : Migration", migration);
        Assert.Contains(result.Id, migration);
        var updated = File.ReadAllText(bundleFile);
        Assert.Contains("// Keep this comment and formatting.", updated);
        Assert.Contains("migrations.Add<Existing>(); // existing registration", updated);
        Assert.Contains("migrations.Add<CreateOrders>();", updated);
        AssertValidCSharp(updated);
    }

    [Fact]
    public void Converts_expression_bodied_configure_and_supports_block_namespace()
    {
        File.WriteAllText(
            bundleFile,
            """
            using SnapData.Migrations;

            namespace App.Migrations
            {
                public sealed class AppMigrationBundle : MigrationBundle
                {
                    protected override void Configure(MigrationCollection items) =>
                        items.Add<Existing>();
                }

                public sealed class Existing : Migration;
            }
            """);

        var result = Scaffolder().Create(Configuration(), "Create orders");

        var updated = File.ReadAllText(bundleFile);
        Assert.Contains("items.Add<Existing>();", updated);
        Assert.Contains("items.Add<CreateOrders>();", updated);
        AssertValidCSharp(updated);
        Assert.Contains(
            "namespace App.Migrations;",
            File.ReadAllText(result.MigrationFile));
    }

    [Fact]
    public void Duplicate_class_fails_without_changing_bundle_or_creating_file()
    {
        var original = """
            using SnapData.Migrations;
            namespace App.Migrations;
            public sealed class AppMigrationBundle : MigrationBundle
            {
                protected override void Configure(MigrationCollection migrations) { }
            }
            public sealed class CreateOrders : Migration;
            """;
        File.WriteAllText(bundleFile, original);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Scaffolder().Create(Configuration(), "Create orders"));

        Assert.Contains("already exists", exception.Message);
        Assert.Equal(original, File.ReadAllText(bundleFile));
        Assert.False(File.Exists(Path.Combine(directory, "CreateOrders.cs")));
    }

    [Fact]
    public void Missing_configured_bundle_fails_without_creating_a_migration()
    {
        File.WriteAllText(
            bundleFile,
            "namespace App.Migrations; public sealed class SomethingElse { }");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Scaffolder().Create(Configuration(), "Create orders"));

        Assert.Contains("was not found", exception.Message);
        Assert.False(File.Exists(Path.Combine(directory, "CreateOrders.cs")));
    }

    [Fact]
    public void Assembly_only_configuration_cannot_be_edited()
    {
        var configuration = Configuration() with
        {
            ProjectPath = null,
            AssemblyPath = Path.Combine(directory, "App.Migrations.dll")
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Scaffolder().Create(configuration, "Create orders"));

        Assert.Contains("prebuilt Assembly", exception.Message);
    }

    private CSharpMigrationScaffolder Scaffolder() => new(
        new MigrationIdGenerator(() => Now));

    private MigrationCliConfiguration Configuration() => new(
        Path.Combine(directory, "snap.ini"),
        null,
        "Sqlite",
        "Data Source=app.db",
        null,
        projectFile,
        "Debug",
        "net8.0",
        "App.Migrations.AppMigrationBundle",
        directory,
        "App.Migrations",
        "__snapdata_migrations",
        MigrationLocking.Required,
        TimeSpan.FromSeconds(30));

    private static void AssertValidCSharp(string source)
    {
        var errors = CSharpSyntaxTree.ParseText(source)
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
