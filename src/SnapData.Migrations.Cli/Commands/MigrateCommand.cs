using System.ComponentModel;
using System.Data.Common;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Runtime;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class MigrateCommand : AsyncCommand<MigrateCommand.Settings>
{
    internal sealed class Settings : ConfiguredCommandSettings
    {
        [CommandOption("-y|--yes")]
        [Description("Apply pending migrations without an interactive confirmation.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var output = new CliOutput(AnsiConsole.Console);
        try
        {
            var configuration = new IniConfigurationLoader().Load(
                Environment.CurrentDirectory,
                settings.Profile,
                settings.ConfigFile);
            var source = configuration.ProjectPath is null
                ? await new MigrationSourceResolver().ResolveAsync(
                    configuration,
                    cancellationToken).ConfigureAwait(false)
                : await output.StatusAsync(
                    $"Building {Path.GetFileName(configuration.ProjectPath)}...",
                    () => new MigrationSourceResolver().ResolveAsync(
                        configuration,
                        cancellationToken)).ConfigureAwait(false);
            var runner = MigrationProviderRegistry.CreateRunner(source);
            var preflight = await output.StatusAsync(
                "Checking migration history...",
                () => MigrationPreflight.EvaluateAsync(
                    runner,
                    source,
                    cancellationToken)).ConfigureAwait(false);
            if (!preflight.IsValid)
            {
                output.WriteMigrationStatus(source, preflight.Status);
                return 2;
            }
            if (preflight.PendingMigrations.Count == 0)
            {
                output.WriteMigrationStatus(source, preflight.Status);
                return 0;
            }

            output.WriteMigrationPlan(source, preflight.PendingMigrations);
            if (!settings.Yes)
            {
                if (Console.IsInputRedirected)
                {
                    output.WriteError(
                        "Interactive confirmation is unavailable. Use --yes to apply pending migrations.");
                    return 1;
                }
                var confirmed = AnsiConsole.Prompt(new ConfirmationPrompt(
                    $"Apply {preflight.PendingMigrations.Count} pending " +
                    "migration(s)?")
                {
                    DefaultValue = false
                });
                if (!confirmed)
                {
                    output.WriteCancelled();
                    return 0;
                }
            }

            await output.StatusAsync(
                $"Applying {preflight.PendingMigrations.Count} migration(s)...",
                () => runner.MigrateAsync(cancellationToken)).ConfigureAwait(false);
            var finalStatus = await output.StatusAsync(
                "Verifying migration status...",
                () => runner.GetStatusAsync(cancellationToken)).ConfigureAwait(false);
            output.WriteMigrationStatus(source, finalStatus);
            return finalStatus.Any(entry => entry.State is
                SnapData.Migrations.MigrationStatusState.Changed or
                SnapData.Migrations.MigrationStatusState.Missing or
                SnapData.Migrations.MigrationStatusState.OutOfOrder) ? 2 : 0;
        }
        catch (Exception exception) when (exception is
            DbException or
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            BadImageFormatException or
            ArgumentException)
        {
            output.WriteError(exception.Message);
            return 1;
        }
    }
}
