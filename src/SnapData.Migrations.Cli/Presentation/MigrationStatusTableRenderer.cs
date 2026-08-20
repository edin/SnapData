using SnapData.Migrations;
using Spectre.Console;

namespace SnapData.Migrations.Cli.Presentation;

internal sealed class MigrationStatusTableRenderer(IAnsiConsole console)
{
    public void Render(IReadOnlyList<MigrationStatusEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey27)
            .Expand()
            .AddColumn(new TableColumn("[grey70]Order[/]").RightAligned())
            .AddColumn("[grey70]Migration[/]")
            .AddColumn(new TableColumn("[grey70]Status[/]").Centered());

        foreach (var entry in entries)
        {
            table.AddRow(
                $"[grey50]{entry.BundleOrder?.ToString() ?? "-"}[/]",
                $"[bold white]{Markup.Escape(entry.MigrationId)}[/]",
                StatusMarkup(entry.State));
        }
        console.Write(table);
    }

    private static string StatusMarkup(MigrationStatusState state) => state switch
    {
        MigrationStatusState.Applied => $"[{CliTheme.Success}]Applied[/]",
        MigrationStatusState.Pending => $"[{CliTheme.Warning}]Pending[/]",
        MigrationStatusState.Changed => $"[bold {CliTheme.Error}]Modified[/]",
        MigrationStatusState.Unverifiable => $"[{CliTheme.Accent}]Unverifiable[/]",
        MigrationStatusState.Missing => $"[bold {CliTheme.Error}]Missing[/]",
        MigrationStatusState.OutOfOrder => $"[bold {CliTheme.Error}]Out of order[/]",
        _ => Markup.Escape(state.ToString())
    };
}
