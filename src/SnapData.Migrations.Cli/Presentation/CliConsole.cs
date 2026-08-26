using Spectre.Console;

namespace SnapData.Migrations.Cli.Presentation;

internal static class CliConsole
{
    public static IAnsiConsole Create(bool noColor)
    {
        var disableColor = ShouldDisableColor(
            noColor,
            Console.IsOutputRedirected,
            Environment.GetEnvironmentVariable("NO_COLOR"));
        if (!disableColor)
        {
            return AnsiConsole.Console;
        }

        var plainOutput = new AnsiStrippingTextWriter(Console.Out);
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(plainOutput)
        });
    }

    internal static bool ShouldDisableColor(
        bool requested,
        bool outputRedirected,
        string? noColorEnvironment) =>
        requested || outputRedirected || noColorEnvironment is not null;
}
