using System.Data.Common;
using SnapData.Migrations;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Runtime;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class StatusCommand : AsyncCommand<StatusCommand.Settings>
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
            var status = await output.StatusAsync(
                "Reading migration status...",
                () => runner.GetStatusAsync(cancellationToken)).ConfigureAwait(false);
            output.WriteMigrationStatus(source, status);

            return status.Any(entry => entry.State is
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
