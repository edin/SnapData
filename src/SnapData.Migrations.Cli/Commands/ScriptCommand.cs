using System.Data.Common;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Runtime;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class ScriptCommand : AsyncCommand<ScriptCommand.Settings>
{
    internal sealed class Settings : ConfiguredCommandSettings;

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

            var scripts = await output.StatusAsync(
                "Planning pending migrations...",
                () => runner.PreviewAsync(
                    preflight.PendingMigrations,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            output.WriteScripts(source, scripts);
            return 0;
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
