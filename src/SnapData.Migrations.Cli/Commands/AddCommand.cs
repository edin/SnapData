using System.ComponentModel;
using SnapData.Migrations.Cli.Configuration;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Scaffolding;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class AddCommand : Command<AddCommand.Settings>
{
    internal sealed class Settings : ConfiguredCommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Migration name, for example CreateOrders.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--combined")]
        [Description("Write Up and Down SQL in one file instead of paired files.")]
        public bool Combined { get; init; }
    }

    protected override int Execute(
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
            if (configuration.ScriptsPath is not null)
            {
                if (configuration.ProjectPath is not null || configuration.AssemblyPath is not null)
                {
                    throw new InvalidOperationException(
                        $"Migration Scripts cannot be combined with Project or Assembly in '{configuration.ConfigFile}'.");
                }
                var result = new SqlMigrationScaffolder().Create(
                    configuration.ScriptsPath,
                    settings.Name,
                    settings.Combined);
                output.WriteMigrationCreated(configuration, result);
                return 0;
            }
            if (settings.Combined)
            {
                throw new InvalidOperationException(
                    "The --combined option is available only for SQL migration workspaces.");
            }
            var csharpResult = new CSharpMigrationScaffolder().Create(
                configuration,
                settings.Name);
            output.WriteMigrationCreated(configuration, csharpResult);
            return 0;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentException)
        {
            output.WriteError(exception.Message);
            return 1;
        }
    }
}
