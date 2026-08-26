using System.ComponentModel;
using System.Data.Common;
using SnapData.Migrations;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Runtime;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class RollbackCommand : AsyncCommand<RollbackCommand.Settings>
{
    internal sealed class Settings : ConfiguredCommandSettings
    {
        [CommandOption("-s|--steps <COUNT>")]
        [DefaultValue(1)]
        [Description("Number of applied migrations to revert.")]
        public int Steps { get; init; } = 1;

        [CommandOption("-y|--yes")]
        [Description("Revert migrations without an interactive confirmation.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var console = CliConsole.Create(settings.NoColor);
        var output = new CliOutput(console);
        try
        {
            if (settings.Steps <= 0)
            {
                output.WriteError("Rollback steps must be greater than zero.");
                return 1;
            }

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
            var runner = MigrationProviderRegistry.CreateRunner(
                source,
                enableRollback: true);
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

            var selected = MigrationPreflight.SelectRollbackMigrations(
                source.Catalog.Migrations,
                preflight.Status,
                settings.Steps);
            if (selected.Count == 0)
            {
                output.WriteMigrationStatus(source, preflight.Status);
                return 0;
            }

            await output.StatusAsync(
                "Planning rollback...",
                () => runner.PreviewAsync(
                    selected,
                    MigrationDirection.Down,
                    cancellationToken)).ConfigureAwait(false);
            output.WriteRollbackPlan(source, selected);
            if (!settings.Yes)
            {
                if (Console.IsInputRedirected)
                {
                    output.WriteError(
                        "Interactive confirmation is unavailable. Use --yes to revert migrations.");
                    return 1;
                }
                var confirmed = console.Prompt(new ConfirmationPrompt(
                    $"Revert {selected.Count} applied migration(s)?")
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
                $"Reverting {selected.Count} migration(s)...",
                () => runner.RollbackAsync(
                    selected.Count,
                    cancellationToken)).ConfigureAwait(false);
            var finalStatus = await output.StatusAsync(
                "Verifying migration status...",
                () => runner.GetStatusAsync(cancellationToken)).ConfigureAwait(false);
            output.WriteMigrationStatus(source, finalStatus);
            return finalStatus.Any(entry => entry.State is
                MigrationStatusState.Changed or
                MigrationStatusState.Missing or
                MigrationStatusState.OutOfOrder) ? 2 : 0;
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
