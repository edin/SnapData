namespace SnapData;

internal interface ICriteriaTokenMatcher
{
    CriteriaToken? Match(CriteriaScanner scanner);
}

internal sealed class IdentifierTokenMatcher : ICriteriaTokenMatcher
{
    public CriteriaToken? Match(CriteriaScanner scanner)
    {
        if (!char.IsLetter(scanner.Current) && scanner.Current != '_')
        {
            return null;
        }

        var position = scanner.Position;
        var line = scanner.Line;
        var column = scanner.Column;
        var value = scanner.TakeWhile(character =>
            char.IsLetterOrDigit(character) || character is '_' or '$');
        var kind = CriteriaTokenMetadataProvider.Keywords.TryGetValue(value, out var keyword)
            ? keyword
            : CriteriaTokenKind.Identifier;
        return scanner.TokenFrom(kind, position, line, column);
    }
}

internal sealed class NumberTokenMatcher : ICriteriaTokenMatcher
{
    public CriteriaToken? Match(CriteriaScanner scanner)
    {
        if (!char.IsDigit(scanner.Current))
        {
            return null;
        }

        var position = scanner.Position;
        var line = scanner.Line;
        var column = scanner.Column;
        scanner.TakeWhile(char.IsDigit);
        if (scanner.Current == '.' && char.IsDigit(scanner.Peek()))
        {
            scanner.Advance();
            scanner.TakeWhile(char.IsDigit);
        }

        if (scanner.Current is 'e' or 'E')
        {
            scanner.Advance();
            if (scanner.Current is '+' or '-')
            {
                scanner.Advance();
            }

            scanner.TakeWhile(char.IsDigit);
        }

        return scanner.TokenFrom(CriteriaTokenKind.Number, position, line, column);
    }
}

internal sealed class StringTokenMatcher : ICriteriaTokenMatcher
{
    public CriteriaToken? Match(CriteriaScanner scanner)
    {
        if (scanner.Current != '\'')
        {
            return null;
        }

        var position = scanner.Position;
        var line = scanner.Line;
        var column = scanner.Column;
        scanner.Advance();
        while (!scanner.IsAtEnd)
        {
            if (scanner.Current == '\'' && scanner.Peek() == '\'')
            {
                scanner.Advance();
                scanner.Advance();
                continue;
            }

            if (scanner.Current == '\'')
            {
                scanner.Advance();
                return scanner.TokenFrom(CriteriaTokenKind.String, position, line, column);
            }

            scanner.Advance();
        }

        var span = scanner.SpanFrom(position, line, column);
        scanner.Report(span, "Unterminated string literal.");
        return scanner.TokenFrom(CriteriaTokenKind.String, position, line, column);
    }
}

internal sealed class ParameterTokenMatcher : ICriteriaTokenMatcher
{
    public CriteriaToken? Match(CriteriaScanner scanner)
    {
        if (scanner.Current is not ('@' or ':')
            || (!char.IsLetter(scanner.Peek()) && scanner.Peek() != '_'))
        {
            return null;
        }

        var position = scanner.Position;
        var line = scanner.Line;
        var column = scanner.Column;
        scanner.Advance();
        scanner.TakeWhile(character => char.IsLetterOrDigit(character) || character == '_');
        return scanner.TokenFrom(CriteriaTokenKind.Parameter, position, line, column);
    }
}

internal sealed class QuotedIdentifierTokenMatcher : ICriteriaTokenMatcher
{
    public CriteriaToken? Match(CriteriaScanner scanner)
    {
        var open = scanner.Current;
        if (open is not ('"' or '`' or '['))
        {
            return null;
        }

        var close = open == '[' ? ']' : open;
        var position = scanner.Position;
        var line = scanner.Line;
        var column = scanner.Column;
        scanner.Advance();
        while (!scanner.IsAtEnd)
        {
            if (scanner.Current == close && scanner.Peek() == close)
            {
                scanner.Advance();
                scanner.Advance();
                continue;
            }

            if (scanner.Current == close)
            {
                scanner.Advance();
                return scanner.TokenFrom(
                    CriteriaTokenKind.QuotedIdentifier,
                    position,
                    line,
                    column);
            }

            scanner.Advance();
        }

        var span = scanner.SpanFrom(position, line, column);
        scanner.Report(span, "Unterminated quoted identifier.");
        return scanner.TokenFrom(CriteriaTokenKind.QuotedIdentifier, position, line, column);
    }
}

internal sealed class CommentTokenMatcher : ICriteriaTokenMatcher
{
    public CriteriaToken? Match(CriteriaScanner scanner)
    {
        if (!scanner.IsAt("--") && !scanner.IsAt("/*"))
        {
            return null;
        }

        var position = scanner.Position;
        var line = scanner.Line;
        var column = scanner.Column;
        if (scanner.TryTake("--"))
        {
            scanner.TakeWhile(character => character is not '\r' and not '\n');
            return scanner.TokenFrom(CriteriaTokenKind.Comment, position, line, column);
        }

        scanner.TryTake("/*");
        while (!scanner.IsAtEnd && !scanner.IsAt("*/"))
        {
            scanner.Advance();
        }

        if (!scanner.TryTake("*/"))
        {
            scanner.Report(
                scanner.SpanFrom(position, line, column),
                "Unterminated block comment.");
        }

        return scanner.TokenFrom(CriteriaTokenKind.Comment, position, line, column);
    }
}

internal sealed class SymbolTokenMatcher(
    IEnumerable<CriteriaTokenMetadata> symbols) : ICriteriaTokenMatcher
{
    private readonly IReadOnlyDictionary<char, IReadOnlyList<CriteriaTokenMetadata>> _symbols =
        symbols.GroupBy(symbol => symbol.Text![0]).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<CriteriaTokenMetadata>)group
                .OrderByDescending(symbol => symbol.Text!.Length)
                .ToArray());

    public CriteriaToken? Match(CriteriaScanner scanner)
    {
        if (!_symbols.TryGetValue(scanner.Current, out var candidates))
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (!scanner.IsAt(candidate.Text!))
            {
                continue;
            }

            var position = scanner.Position;
            var line = scanner.Line;
            var column = scanner.Column;
            scanner.TryTake(candidate.Text!);
            return scanner.TokenFrom(candidate.Kind, position, line, column);
        }

        return null;
    }
}
