using System.Reflection;

namespace SnapData;

internal enum CriteriaTokenKind
{
    [CriteriaToken(CriteriaTokenGroup.Identifier, typeof(IdentifierTokenMatcher))]
    Identifier,
    [CriteriaToken(CriteriaTokenGroup.Identifier, typeof(QuotedIdentifierTokenMatcher))]
    QuotedIdentifier,
    [CriteriaToken(CriteriaTokenGroup.Literal, typeof(NumberTokenMatcher))]
    Number,
    [CriteriaToken(CriteriaTokenGroup.Literal, typeof(StringTokenMatcher))]
    String,
    [CriteriaToken(CriteriaTokenGroup.Parameter, typeof(ParameterTokenMatcher))]
    Parameter,

    [CriteriaKeyword("AND")]
    And,
    [CriteriaKeyword("OR")]
    Or,
    [CriteriaKeyword("NOT")]
    Not,
    [CriteriaKeyword("IS")]
    Is,
    [CriteriaKeyword("NULL")]
    Null,
    [CriteriaKeyword("TRUE")]
    True,
    [CriteriaKeyword("FALSE")]
    False,
    [CriteriaKeyword("IN")]
    In,
    [CriteriaKeyword("BETWEEN")]
    Between,
    [CriteriaKeyword("LIKE")]
    Like,
    [CriteriaKeyword("ON")]
    On,
    [CriteriaKeyword("AS")]
    As,

    [CriteriaSymbol("<>")]
    NotEqualSql,
    [CriteriaSymbol("!=")]
    NotEqual,
    [CriteriaSymbol("<=")]
    LessThanOrEqual,
    [CriteriaSymbol(">=")]
    GreaterThanOrEqual,
    [CriteriaSymbol("=")]
    Equal,
    [CriteriaSymbol("<")]
    LessThan,
    [CriteriaSymbol(">")]
    GreaterThan,
    [CriteriaSymbol("(")]
    LeftParenthesis,
    [CriteriaSymbol(")")]
    RightParenthesis,
    [CriteriaSymbol(",")]
    Comma,
    [CriteriaSymbol(".")]
    Dot,
    [CriteriaSymbol("+")]
    Plus,
    [CriteriaSymbol("-")]
    Minus,
    [CriteriaSymbol("*")]
    Star,
    [CriteriaSymbol("/")]
    Slash,
    [CriteriaSymbol("%")]
    Percent,

    [CriteriaToken(CriteriaTokenGroup.Trivia, typeof(CommentTokenMatcher))]
    Comment,
    [CriteriaToken(CriteriaTokenGroup.EndOfFile)]
    EndOfFile
}

internal enum CriteriaTokenGroup
{
    Identifier,
    Literal,
    Parameter,
    Keyword,
    Symbol,
    Trivia,
    EndOfFile
}

[AttributeUsage(AttributeTargets.Field)]
internal class CriteriaTokenAttribute : Attribute
{
    internal CriteriaTokenAttribute(
        CriteriaTokenGroup group,
        Type? matcherType = null,
        string? text = null)
    {
        if (matcherType is not null && !typeof(ICriteriaTokenMatcher).IsAssignableFrom(matcherType))
        {
            throw new ArgumentException(
                $"Matcher type '{matcherType.FullName}' must implement {nameof(ICriteriaTokenMatcher)}.",
                nameof(matcherType));
        }

        Group = group;
        MatcherType = matcherType;
        Text = text;
    }

    internal CriteriaTokenGroup Group { get; }

    internal Type? MatcherType { get; }

    internal string? Text { get; }
}

internal sealed class CriteriaKeywordAttribute(string text)
    : CriteriaTokenAttribute(CriteriaTokenGroup.Keyword, text: text);

internal sealed class CriteriaSymbolAttribute(string text)
    : CriteriaTokenAttribute(CriteriaTokenGroup.Symbol, text: text);

internal sealed record CriteriaTokenMetadata(
    CriteriaTokenKind Kind,
    CriteriaTokenGroup Group,
    string? Text,
    Type? MatcherType);

internal static class CriteriaTokenMetadataProvider
{
    internal static readonly IReadOnlyList<CriteriaTokenMetadata> All =
        Enum.GetValues<CriteriaTokenKind>().Select(Read).ToArray();

    internal static readonly IReadOnlyDictionary<CriteriaTokenKind, CriteriaTokenMetadata> ByKind =
        All.ToDictionary(metadata => metadata.Kind);

    internal static readonly IReadOnlyDictionary<string, CriteriaTokenKind> Keywords =
        All.Where(metadata => metadata.Group == CriteriaTokenGroup.Keyword)
            .ToDictionary(
                metadata => metadata.Text!,
                metadata => metadata.Kind,
                StringComparer.OrdinalIgnoreCase);

    internal static readonly IReadOnlyList<CriteriaTokenMetadata> Symbols =
        All.Where(metadata => metadata.Group == CriteriaTokenGroup.Symbol)
            .OrderByDescending(metadata => metadata.Text!.Length)
            .ToArray();

    private static CriteriaTokenMetadata Read(CriteriaTokenKind kind)
    {
        var field = typeof(CriteriaTokenKind).GetField(kind.ToString())
            ?? throw new InvalidOperationException($"Missing enum field for {kind}.");
        var attribute = field.GetCustomAttribute<CriteriaTokenAttribute>()
            ?? throw new InvalidOperationException(
                $"Criteria token {kind} is missing {nameof(CriteriaTokenAttribute)}.");
        return new CriteriaTokenMetadata(
            kind,
            attribute.Group,
            attribute.Text,
            attribute.MatcherType);
    }
}

internal readonly record struct CriteriaSourceSpan(
    int Position,
    int Length,
    int Line,
    int Column)
{
    internal int End => Position + Length;
}

internal sealed record CriteriaToken(
    CriteriaTokenKind Kind,
    string Value,
    CriteriaSourceSpan Span);

internal sealed record CriteriaDiagnostic(string Message, CriteriaSourceSpan Span);

internal sealed record CriteriaScanResult(
    IReadOnlyList<CriteriaToken> Tokens,
    IReadOnlyList<CriteriaDiagnostic> Diagnostics)
{
    internal bool Succeeded => Diagnostics.Count == 0;
}
