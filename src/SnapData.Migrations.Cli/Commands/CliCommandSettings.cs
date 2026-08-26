using System.ComponentModel;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal abstract class CliCommandSettings : CommandSettings
{
    [CommandOption("--no-color")]
    [Description("Disable ANSI colors and styling.")]
    public bool NoColor { get; init; }
}
