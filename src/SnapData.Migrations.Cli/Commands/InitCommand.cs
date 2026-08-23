using System.ComponentModel;
using SnapData.Migrations.Cli.Presentation;
using SnapData.Migrations.Cli.Scaffolding;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class InitCommand : Command<InitCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("-d|--directory <PATH>")]
        [Description("Directory to initialize. Defaults to the current directory.")]
        public string? Directory { get; init; }

        [CommandOption("--sql")]
        [Description("Create a project-free SQL migration workspace.")]
        public bool Sql { get; init; }
    }

    protected override int Execute(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var output = new CliOutput(AnsiConsole.Console);
        try
        {
            var target = settings.Directory is null
                ? Environment.CurrentDirectory
                : Path.GetFullPath(settings.Directory, Environment.CurrentDirectory);
            var scaffolder = new MigrationScaffolder();
            var result = settings.Sql
                ? scaffolder.CreateSql(target)
                : scaffolder.Create(target);
            output.WriteInitialization(result);
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
