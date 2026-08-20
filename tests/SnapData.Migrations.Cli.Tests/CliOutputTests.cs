using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Discovery;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Runtime;
using SnapData.Migrations;
using Spectre.Console;

namespace SnapData.Migrations.Cli.Tests;

public sealed class CliOutputTests
{
    [Fact]
    public void Migration_list_shows_context_and_never_prints_connection_secret()
    {
        var writer = new StringWriter();
        var console = CreateConsole(writer);
        var configuration = new MigrationCliConfiguration(
            "C:/app/snap.ini",
            "testing",
            "Sqlite",
            "Data Source=secret.db;Password=do-not-print",
            "C:/app/bin/App.Migrations.dll",
            "C:/app/App.Migrations.csproj",
            "Debug",
            "net8.0",
            "App.Migrations.AppBundle",
            null,
            "App.Migrations",
            "__snapdata_migrations",
            TimeSpan.FromSeconds(30));

        new CliOutput(console).WriteMigrationList(
            configuration,
            configuration.AssemblyPath!,
            new MigrationCatalog(
                [new MigrationDescriptor("001-create-users", "App.Migrations.CreateUsers")],
                "App.Migrations.AppBundle"));

        var output = writer.ToString();
        Assert.Contains("SnapData migrations", output);
        Assert.Contains("App.Migrations.csproj", output);
        Assert.Contains("net8.0", output);
        Assert.Contains("testing", output);
        Assert.Contains("001-create-users", output);
        Assert.Contains("AppBundle", output);
        Assert.Contains("1 migration discovered", output);
        Assert.DoesNotContain("do-not-print", output);
        Assert.DoesNotContain("secret.db", output);
    }

    [Fact]
    public void Error_output_escapes_user_supplied_markup()
    {
        var writer = new StringWriter();

        new CliOutput(CreateConsole(writer)).WriteError("Missing [Migration] section.");

        Assert.Contains("Missing [Migration] section", writer.ToString());
    }

    [Fact]
    public void Status_output_renders_states_and_summary()
    {
        var writer = new StringWriter();
        var configuration = new MigrationCliConfiguration(
            "C:/app/snap.ini",
            null,
            "Postgres",
            "Host=secret;Password=do-not-print",
            "C:/app/App.Migrations.dll",
            null,
            "Debug",
            null,
            null,
            null,
            "App.Migrations",
            "__snapdata_migrations",
            TimeSpan.FromSeconds(30));
        var catalog = new MigrationCatalog(
            [
                new MigrationDescriptor("001-users", "App.Migrations.Users"),
                new MigrationDescriptor("002-email", "App.Migrations.Email")
            ],
            null);
        var source = new MigrationSource(
            configuration,
            configuration.AssemblyPath!,
            catalog);

        new CliOutput(CreateConsole(writer)).WriteMigrationStatus(
            source,
            [
                new MigrationStatusEntry(
                    "001-users", MigrationStatusState.Changed, BundleOrder: 1),
                new MigrationStatusEntry(
                    "002-email", MigrationStatusState.Pending, BundleOrder: 2)
            ]);

        var output = writer.ToString();
        Assert.Contains("Migration status", output);
        Assert.Contains("Modified", output);
        Assert.Contains("Pending", output);
        Assert.Contains("1 migration history issue", output);
        Assert.DoesNotContain("do-not-print", output);
    }

    private static IAnsiConsole CreateConsole(StringWriter writer) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer)
        });
}
