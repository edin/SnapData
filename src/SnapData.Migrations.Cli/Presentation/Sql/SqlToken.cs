namespace SnapData.Migrations.Cli.Presentation.Sql;

internal enum SqlTokenType
{
    Keyword,
    Type,
    Function,
    Identifier,
    String,
    Number,
    Symbol,
    Whitespace,
    Comment,
    Other
}

internal readonly record struct SqlToken(SqlTokenType Type, string Value);
