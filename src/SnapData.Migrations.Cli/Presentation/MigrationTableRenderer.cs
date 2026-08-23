using SnapData.Migrations.Cli.Discovery;
using Spectre.Console;

namespace SnapData.Migrations.Cli.Presentation;

internal sealed class MigrationTableRenderer(IAnsiConsole console)
{
    public void Render(
        IReadOnlyList<MigrationDescriptor> migrations,
        string detailHeader = "Class")
    {
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailHeader);
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey27)
            .Expand()
            .AddColumn(new TableColumn("[grey70]#[/]").RightAligned())
            .AddColumn("[grey70]Migration[/]")
            .AddColumn($"[grey70]{Markup.Escape(detailHeader)}[/]");

        for (var index = 0; index < migrations.Count; index++)
        {
            var migration = migrations[index];
            table.AddRow(
                $"[grey50]{index + 1}[/]",
                $"[bold white]{Markup.Escape(migration.Id)}[/]",
                $"[grey70]{Markup.Escape(ShortTypeName(migration.TypeName))}[/]");
        }

        console.Write(table);
    }

    private static string ShortTypeName(string typeName)
    {
        var separator = Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('+'));
        return separator < 0 ? typeName : typeName[(separator + 1)..];
    }
}
