using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Discovery;
using SnapData.Migrations.Cli.Runtime;
using SnapData.Migrations.Cli.Scaffolding;
using SnapData.Migrations;
using Spectre.Console;

namespace SnapData.Migrations.Cli.Presentation;

internal sealed class CliOutput(IAnsiConsole console)
{
    public async Task<T> StatusAsync<T>(string message, Func<Task<T>> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(action);
        return await console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(CliTheme.Accent))
            .StartAsync(message, _ => action())
            .ConfigureAwait(false);
    }

    public async Task StatusAsync(string message, Func<Task> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(action);
        await console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(CliTheme.Accent))
            .StartAsync(message, _ => action())
            .ConfigureAwait(false);
    }

    public void WriteMigrationList(
        MigrationCliConfiguration configuration,
        string assemblyPath,
        MigrationCatalog migrations)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(migrations);

        console.WriteLine();
        console.Write(new Rule($"[bold {CliTheme.Accent}]SnapData migrations[/]")
            .RuleStyle(CliTheme.Border)
            .LeftJustified());
        console.WriteLine();
        WriteConfiguration(configuration, assemblyPath, migrations.BundleType);
        console.WriteLine();

        if (migrations.Count == 0)
        {
            WriteWarning("No migrations were discovered.");
            return;
        }

        new MigrationTableRenderer(console).Render(migrations);
        console.WriteLine();
        var noun = migrations.Count == 1 ? "migration" : "migrations";
        WriteSuccess($"{migrations.Count} {noun} discovered.");
        console.WriteLine();
    }

    public void WriteError(string message) =>
        WriteBadge("Error", CliTheme.Error, message);

    public void WriteInitialization(MigrationScaffoldResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        console.WriteLine();
        console.Write(new Rule($"[bold {CliTheme.Accent}]SnapData initialized[/]")
            .RuleStyle(CliTheme.Border)
            .LeftJustified());
        console.WriteLine();
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn();
        grid.AddRow("[grey50]Config[/]", Markup.Escape(
            Path.GetRelativePath(result.Directory, result.ConfigFile)));
        grid.AddRow("[grey50]Project[/]", Markup.Escape(
            Path.GetRelativePath(result.Directory, result.ProjectFile)));
        grid.AddRow("[grey50]Migrations[/]", Markup.Escape(
            Path.GetDirectoryName(Path.GetRelativePath(
                result.Directory, result.MigrationFiles[0])) ?? "Migrations"));
        console.Write(grid);
        console.WriteLine();
        WriteSuccess("Ready. Run 'snapdata list' to discover the sample migrations.");
        console.WriteLine();
    }

    public void WriteMigrationStatus(
        MigrationSource source,
        IReadOnlyList<MigrationStatusEntry> status)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(status);
        console.WriteLine();
        console.Write(new Rule($"[bold {CliTheme.Accent}]Migration status[/]")
            .RuleStyle(CliTheme.Border)
            .LeftJustified());
        console.WriteLine();
        WriteConfiguration(
            source.Configuration,
            source.AssemblyPath,
            source.Catalog.BundleType);
        console.WriteLine();

        if (status.Count == 0)
        {
            WriteWarning("No migrations were discovered.");
            return;
        }

        new MigrationStatusTableRenderer(console).Render(status);
        console.WriteLine();
        var applied = status.Count(entry => entry.State == MigrationStatusState.Applied);
        var pending = status.Count(entry => entry.State == MigrationStatusState.Pending);
        var unverifiable = status.Count(entry =>
            entry.State == MigrationStatusState.Unverifiable);
        var invalid = status.Count(entry => entry.State is
            MigrationStatusState.Changed or
            MigrationStatusState.Missing or
            MigrationStatusState.OutOfOrder);

        if (invalid > 0)
        {
            WriteError($"{invalid} migration history issue{Plural(invalid)} detected.");
        }
        if (pending > 0)
        {
            WriteWarning($"{pending} pending migration{Plural(pending)}.");
        }
        if (unverifiable > 0)
        {
            WriteWarning(
                $"{unverifiable} applied migration{Plural(unverifiable)} use schema-dependent plans and cannot be fingerprint-verified.");
        }
        if (invalid == 0 && pending == 0)
        {
            WriteSuccess($"Database is up to date; {applied + unverifiable} migrations applied.");
        }
        else if (applied > 0)
        {
            console.MarkupLine(
                $"  [grey50]Applied[/] [{CliTheme.Muted}]{applied}[/]");
        }
        console.WriteLine();
    }

    public void WriteScripts(
        MigrationSource source,
        IReadOnlyList<MigrationScript> scripts)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(scripts);
        console.WriteLine();
        console.Write(new Rule($"[bold {CliTheme.Accent}]Pending migration SQL[/]")
            .RuleStyle(CliTheme.Border)
            .LeftJustified());
        console.WriteLine();
        WriteConfiguration(
            source.Configuration,
            source.AssemblyPath,
            source.Catalog.BundleType);
        console.WriteLine();
        new MigrationScriptRenderer(console).Render(scripts);
        var statements = scripts.Sum(script => script.Statements.Count);
        WriteSuccess(
            $"Generated {statements} SQL statement{Plural(statements)} for " +
            $"{scripts.Count} pending migration{Plural(scripts.Count)}.");
        console.WriteLine();
    }

    public void WriteMigrationPlan(
        MigrationSource source,
        IReadOnlyList<Migration> pendingMigrations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pendingMigrations);
        console.WriteLine();
        console.Write(new Rule($"[bold {CliTheme.Accent}]Migration plan[/]")
            .RuleStyle(CliTheme.Border)
            .LeftJustified());
        console.WriteLine();
        WriteConfiguration(
            source.Configuration,
            source.AssemblyPath,
            source.Catalog.BundleType);
        console.WriteLine();
        new MigrationTableRenderer(console).Render(
            new MigrationCatalog(pendingMigrations, source.Catalog.BundleType));
        console.WriteLine();
        WriteWarning(
            $"{pendingMigrations.Count} migration{Plural(pendingMigrations.Count)} will be applied.");
        console.WriteLine();
    }

    public void WriteRollbackPlan(
        MigrationSource source,
        IReadOnlyList<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(migrations);
        console.WriteLine();
        console.Write(new Rule($"[bold {CliTheme.Warning}]Rollback plan[/]")
            .RuleStyle(CliTheme.Border)
            .LeftJustified());
        console.WriteLine();
        WriteConfiguration(
            source.Configuration,
            source.AssemblyPath,
            source.Catalog.BundleType);
        console.WriteLine();
        new MigrationTableRenderer(console).Render(
            new MigrationCatalog(migrations, source.Catalog.BundleType));
        console.WriteLine();
        WriteWarning(
            $"{migrations.Count} migration{Plural(migrations.Count)} will be reverted.");
        console.WriteLine();
    }

    public void WriteCancelled() => WriteWarning("Operation cancelled; no changes were made.");

    public void WriteWarning(string message) =>
        WriteBadge("Warning", CliTheme.Warning, message, darkText: true);

    public void WriteSuccess(string message) =>
        WriteBadge("OK", CliTheme.Success, message);

    private void WriteConfiguration(
        MigrationCliConfiguration configuration,
        string assemblyPath,
        string? discoveredBundleType)
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn();
        if (configuration.ProjectPath is not null)
        {
            var projectDirectory = Path.GetDirectoryName(configuration.ProjectPath)!;
            grid.AddRow(
                "[grey50]Project[/]",
                Markup.Escape(Path.GetFileName(configuration.ProjectPath)));
            grid.AddRow(
                "[grey50]Output[/]",
                Markup.Escape(Path.GetRelativePath(projectDirectory, assemblyPath)));
        }
        else
        {
            grid.AddRow(
                "[grey50]Assembly[/]",
                Markup.Escape(Path.GetFileName(assemblyPath)));
        }
        if (configuration.TargetFramework is not null)
        {
            grid.AddRow(
                "[grey50]Framework[/]",
                Markup.Escape(configuration.TargetFramework));
        }
        if (configuration.Profile is not null)
        {
            grid.AddRow("[grey50]Profile[/]", Markup.Escape(configuration.Profile));
        }
        if (!string.IsNullOrWhiteSpace(configuration.Provider))
        {
            grid.AddRow("[grey50]Provider[/]", Markup.Escape(configuration.Provider));
        }
        if (configuration.MigrationsNamespace is not null)
        {
            grid.AddRow(
                "[grey50]Namespace[/]",
                Markup.Escape(configuration.MigrationsNamespace));
        }
        grid.AddRow(
            "[grey50]Bundle[/]",
            Markup.Escape(ShortTypeName(
                discoveredBundleType ?? configuration.BundleType ?? "Convention scan")));
        console.Write(grid);
    }

    private static string ShortTypeName(string typeName)
    {
        var separator = Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('+'));
        return separator < 0 ? typeName : typeName[(separator + 1)..];
    }

    private void WriteBadge(
        string label,
        string color,
        string message,
        bool darkText = false)
    {
        var foreground = darkText ? "black" : "white";
        console.MarkupLine(
            $"  [bold {foreground} on {color}] {Markup.Escape(label)} [/] " +
            $"[{CliTheme.Muted}]{Markup.Escape(message)}[/]");
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
