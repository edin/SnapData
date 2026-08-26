using Spectre.Console;

namespace SnapData.Migrations.Cli.Presentation;

internal static class CliConsole
{
    public static IAnsiConsole Create(bool noColor)
    {
        if (!ShouldDisableColor(
                noColor,
                Console.IsOutputRedirected,
                Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            return AnsiConsole.Console;
        }

        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(Console.Out)
        });
    }

    internal static bool ShouldDisableColor(
        bool requested,
        bool outputRedirected,
        string? noColorEnvironment) =>
        requested || outputRedirected || noColorEnvironment is not null;
}
