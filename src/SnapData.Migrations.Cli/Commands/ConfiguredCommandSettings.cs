using System.ComponentModel;
using Spectre.Console.Cli;

namespace SnapData.Migrations.Cli.Commands;

internal abstract class ConfiguredCommandSettings : CommandSettings
{
    [CommandOption("-c|--config <FILE>")]
    [Description("Path to a migration INI file. Defaults to upward snap.ini discovery.")]
    public string? ConfigFile { get; init; }

    [CommandOption("-p|--profile <PROFILE>")]
    [Description("Named migration profile from the INI configuration.")]
    public string? Profile { get; init; }
}
