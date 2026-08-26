using SnapData.Migrations.Cli.Presentation;

namespace SnapData.Migrations.Cli.Tests;

public sealed class CliConsoleTests
{
    [Theory]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    [InlineData(false, false, "")]
    [InlineData(false, false, "1")]
    public void Disables_color_when_requested_redirected_or_no_color_is_present(
        bool requested,
        bool redirected,
        string? environmentValue)
    {
        Assert.True(CliConsole.ShouldDisableColor(
            requested,
            redirected,
            environmentValue));
    }

    [Fact]
    public void Keeps_color_for_an_interactive_terminal_by_default()
    {
        Assert.False(CliConsole.ShouldDisableColor(
            requested: false,
            outputRedirected: false,
            noColorEnvironment: null));
    }
}
