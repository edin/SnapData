using System.Text;
using Spectre.Console;

namespace SnapData.Migrations.Cli.Presentation.Sql;

internal static class SqlHighlighter
{
    public static Markup Highlight(string sql) => new(ToMarkup(sql));

    internal static string ToMarkup(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var result = new StringBuilder(sql.Length * 2);
        foreach (var token in SqlLexer.Tokenize(sql))
        {
            var value = Markup.Escape(token.Value);
            result.Append(token.Type switch
            {
                SqlTokenType.Keyword => $"[purple_2]{value}[/]",
                SqlTokenType.Type => $"[{CliTheme.Accent}]{value}[/]",
                SqlTokenType.Function => $"[mediumvioletred]{value}[/]",
                SqlTokenType.Identifier => $"[lightsteelblue3]{value}[/]",
                SqlTokenType.String => $"[{CliTheme.Success}]{value}[/]",
                SqlTokenType.Number => $"[cyan]{value}[/]",
                SqlTokenType.Comment => $"[grey50]{value}[/]",
                _ => value
            });
        }
        return result.ToString();
    }
}
