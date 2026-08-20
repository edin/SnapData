using System.Text.RegularExpressions;

namespace SnapData.Migrations.Cli.Presentation.Sql;

internal static partial class SqlLexer
{
    public static IReadOnlyList<SqlToken> Tokenize(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var tokens = new List<SqlToken>();
        foreach (Match match in TokenPattern().Matches(sql))
        {
            tokens.Add(match.Groups["Comment"].Success
                ? new SqlToken(SqlTokenType.Comment, match.Value)
                : match.Groups["String"].Success
                    ? new SqlToken(SqlTokenType.String, match.Value)
                    : match.Groups["Number"].Success
                        ? new SqlToken(SqlTokenType.Number, match.Value)
                        : match.Groups["Identifier"].Success
                            ? ClassifyIdentifier(match.Value)
                            : match.Groups["Symbol"].Success
                                ? new SqlToken(SqlTokenType.Symbol, match.Value)
                                : match.Groups["Whitespace"].Success
                                    ? new SqlToken(SqlTokenType.Whitespace, match.Value)
                                    : new SqlToken(SqlTokenType.Other, match.Value));
        }
        return tokens;
    }

    private static SqlToken ClassifyIdentifier(string value)
    {
        if (IsQuotedIdentifier(value))
        {
            return new SqlToken(SqlTokenType.Identifier, value);
        }
        if (SqlLanguage.Keywords.Contains(value))
        {
            return new SqlToken(SqlTokenType.Keyword, value);
        }
        if (SqlLanguage.Types.Contains(value))
        {
            return new SqlToken(SqlTokenType.Type, value);
        }
        return SqlLanguage.Functions.Contains(value)
            ? new SqlToken(SqlTokenType.Function, value)
            : new SqlToken(SqlTokenType.Identifier, value);
    }

    private static bool IsQuotedIdentifier(string value) =>
        value.Length >= 2
        && ((value[0] == '"' && value[^1] == '"')
            || (value[0] == '`' && value[^1] == '`')
            || (value[0] == '[' && value[^1] == ']'));

    [GeneratedRegex(
        "(?<Comment>--[^\\r\\n]*|/\\*[\\s\\S]*?\\*/)" +
        "|(?<String>[Nn]?'(?:''|[^'])*')" +
        "|(?<Number>\\b\\d+(?:\\.\\d+)?\\b)" +
        "|(?<Identifier>\"(?:\"\"|[^\"])*\"|`(?:``|[^`])*`|\\[(?:\\]\\]|[^\\]])*\\]|[A-Za-z_][A-Za-z0-9_$]*)" +
        "|(?<Symbol>[(),;.*])" +
        "|(?<Whitespace>\\s+)" +
        "|(?<Other>.)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
