using System.ComponentModel;
using System.Data.Common;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Runtime;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class ScriptCommand : AsyncCommand<ScriptCommand.Settings>
{
    internal sealed class Settings : ConfiguredCommandSettings
    {
        [CommandOption("-o|--output <FILE>")]
        [Description("Write pending migration SQL to a plain deployment file.")]
        public string? OutputFile { get; init; }

        [CommandOption("--force")]
        [Description("Replace the output file if it already exists.")]
        public bool Force { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var output = new CliOutput(CliConsole.Create(settings.NoColor));
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
            if (settings.OutputFile is null)
            {
                output.WriteScripts(source, scripts);
            }
            else
            {
                var outputFile = Path.GetFullPath(
                    settings.OutputFile,
                    Environment.CurrentDirectory);
                new MigrationScriptFileWriter().Write(
                    outputFile,
                    scripts,
                    settings.Force);
                output.WriteScriptWritten(outputFile, scripts);
            }
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
