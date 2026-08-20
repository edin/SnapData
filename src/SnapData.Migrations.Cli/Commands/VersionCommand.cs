using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class VersionCommand : Command<VersionCommand.Settings>
{
    internal sealed class Settings : CommandSettings;

    protected override int Execute(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine($"snapdata {CliVersion.Value}");
        return 0;
    }
}
