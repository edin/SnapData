using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Runtime;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class ListCommand : AsyncCommand<ListCommand.Settings>
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
            output.WriteMigrationList(
                configuration,
                source.AssemblyPath,
                source.Catalog);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or BadImageFormatException)
        {
            output.WriteError(exception.Message);
            return 1;
        }
    }
}
