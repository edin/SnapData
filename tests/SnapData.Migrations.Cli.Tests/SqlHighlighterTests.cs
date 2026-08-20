using SnapData.Migrations.Cli.Presentation.Sql;
using Spectre.Console;

namespace SnapData.Migrations.Cli.Tests;

public sealed class SqlHighlighterTests
{
    [Fact]
    public void Highlighted_sql_renders_to_the_exact_original_text()
    {
        const string sql =
            "CREATE TABLE [user]]log] (name VARCHAR(40) DEFAULT 'a[b]');\n" +
            "-- preserve [markup]\nSELECT COUNT(*) FROM `user`;";
        var writer = new StringWriter();
        var console = CreateConsole(writer);

        Assert.Equal(
            sql,
            string.Concat(SqlLexer.Tokenize(sql).Select(token => token.Value)));
        console.Write(SqlHighlighter.Highlight(sql));

        Assert.Equal(sql.ReplaceLineEndings(), writer.ToString());
    }

    [Fact]
    public void Classifies_common_migration_sql_tokens()
    {
        var tokens = SqlLexer.Tokenize(
            "ALTER TABLE users ADD created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP");

        Assert.Contains(tokens, token =>
            token is { Type: SqlTokenType.Keyword, Value: "ALTER" });
        Assert.Contains(tokens, token =>
            token is { Type: SqlTokenType.Type, Value: "TIMESTAMP" });
        Assert.Contains(tokens, token =>
            token is { Type: SqlTokenType.Function, Value: "CURRENT_TIMESTAMP" });
        Assert.Contains(tokens, token =>
            token is { Type: SqlTokenType.Identifier, Value: "created_at" });
    }

    [Fact]
    public void Escapes_markup_after_tokenization()
    {
        var markup = SqlHighlighter.ToMarkup(
            "SELECT '[red]not markup[/]' FROM [events]");

        _ = new Markup(markup);
        Assert.Contains("[[red]]not markup[[/]]", markup);
        Assert.Contains("[[events]]", markup);
    }

    private static IAnsiConsole CreateConsole(StringWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 200;
        return console;
    }
}
