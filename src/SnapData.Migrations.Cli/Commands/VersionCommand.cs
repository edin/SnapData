using SnapData.Migrations.Cli.Presentation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal sealed class VersionCommand : Command<VersionCommand.Settings>
{
    internal sealed class Settings : CliCommandSettings;

    protected override int Execute(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        CliConsole.Create(settings.NoColor).WriteLine($"snapdata {CliVersion.Value}");
        return 0;
    }
}
