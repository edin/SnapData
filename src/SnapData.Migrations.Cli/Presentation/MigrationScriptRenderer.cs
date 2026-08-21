using SnapData.Migrations;
using SnapData.Migrations.Cli.Presentation.Sql;
using Spectre.Console;

namespace SnapData.Migrations.Cli.Presentation;

internal sealed class MigrationScriptRenderer(IAnsiConsole console)
{
    public void Render(IReadOnlyList<MigrationScript> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        foreach (var script in scripts)
        {
            console.Write(new Rule(
                    $"[bold white]{Markup.Escape(script.MigrationId)}[/]")
                .RuleStyle(CliTheme.Border)
                .LeftJustified());
            if (script.Statements.Count == 0)
            {
                console.MarkupLine("[grey50]-- no SQL statements[/]");
                console.WriteLine();
                continue;
            }

            for (var index = 0; index < script.Statements.Count; index++)
            {
                var statement = script.Statements[index];
                var panel = new Panel(SqlHighlighter.Highlight(statement.Sql))
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Grey27)
                    .Padding(1, 0)
                    .Expand();
                if (script.Statements.Count > 1)
                {
                    panel.Header = new PanelHeader(
                        $" [grey70]{index + 1}/{script.Statements.Count}[/] ");
                }
                console.Write(panel);
            }
            console.WriteLine();
        }
    }
}
