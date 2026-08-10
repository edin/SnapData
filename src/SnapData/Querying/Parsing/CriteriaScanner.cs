namespace SnapData;

internal sealed class CriteriaScanner
{
    private static readonly IReadOnlyList<ICriteriaTokenMatcher> Matchers = BuildMatchers();
    private readonly string _source;
    private readonly List<CriteriaDiagnostic> _diagnostics = [];
    private int _position;
    private int _line = 1;
    private int _column = 1;

    internal CriteriaScanner(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    internal int Position => _position;

    internal int Line => _line;

    internal int Column => _column;

    internal bool IsAtEnd => _position >= _source.Length;

    internal char Current => IsAtEnd ? '\0' : _source[_position];

    internal char Peek(int offset = 1)
    {
        var index = _position + offset;
        return index >= 0 && index < _source.Length ? _source[index] : '\0';
    }

    internal bool IsAt(string text) =>
        !string.IsNullOrEmpty(text)
        && _position + text.Length <= _source.Length
        && string.Compare(_source, _position, text, 0, text.Length, StringComparison.Ordinal) == 0;

    internal bool TryTake(string text)
    {
        if (!IsAt(text))
        {
            return false;
        }

        foreach (var _ in text)
        {
            Advance();
        }

        return true;
    }

    internal void Advance()
    {
        if (IsAtEnd)
        {
            return;
        }

        if (Current == '\r' && Peek() == '\n')
        {
            _position++;
            _line++;
            _column = 1;
        }
        else if (Current is '\r' or '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        _position++;
    }

    internal string TakeWhile(Func<char, bool> predicate)
    {
        var start = _position;
        while (!IsAtEnd && predicate(Current))
        {
            Advance();
        }

        return _source[start.._position];
    }

    internal void Report(CriteriaSourceSpan span, string message) =>
        _diagnostics.Add(new CriteriaDiagnostic(message, span));

    internal CriteriaSourceSpan SpanFrom(int position, int line, int column) =>
        new(position, _position - position, line, column);

    internal CriteriaToken TokenFrom(
        CriteriaTokenKind kind,
        int position,
        int line,
        int column) =>
        new(kind, _source[position.._position], SpanFrom(position, line, column));

    internal CriteriaScanResult Scan()
    {
        var tokens = new List<CriteriaToken>();
        while (!IsAtEnd)
        {
            if (char.IsWhiteSpace(Current))
            {
                Advance();
                continue;
            }

            CriteriaToken? token = null;
            foreach (var matcher in Matchers)
            {
                token = matcher.Match(this);
                if (token is not null)
                {
                    break;
                }
            }

            if (token is null)
            {
                var span = new CriteriaSourceSpan(_position, 1, _line, _column);
                Report(span, $"Unexpected character '{Current}'.");
                Advance();
                continue;
            }

            if (CriteriaTokenMetadataProvider.ByKind[token.Kind].Group != CriteriaTokenGroup.Trivia)
            {
                tokens.Add(token);
            }
        }

        tokens.Add(new CriteriaToken(
            CriteriaTokenKind.EndOfFile,
            string.Empty,
            new CriteriaSourceSpan(_position, 0, _line, _column)));
        return new CriteriaScanResult(tokens, _diagnostics);
    }

    private static IReadOnlyList<ICriteriaTokenMatcher> BuildMatchers()
    {
        var matchers = CriteriaTokenMetadataProvider.All
            .Where(metadata => metadata.MatcherType is not null)
            .GroupBy(metadata => metadata.MatcherType)
            .Select(group => group.First())
            .OrderBy(metadata => metadata.Group switch
            {
                CriteriaTokenGroup.Trivia => 0,
                CriteriaTokenGroup.Literal => 1,
                CriteriaTokenGroup.Parameter => 2,
                CriteriaTokenGroup.Identifier => 3,
                _ => 4
            })
            .Select(metadata => Activator.CreateInstance(metadata.MatcherType!) as ICriteriaTokenMatcher
                ?? throw new InvalidOperationException(
                    $"Could not create token matcher '{metadata.MatcherType!.FullName}'."))
            .ToList();
        matchers.Add(new SymbolTokenMatcher(CriteriaTokenMetadataProvider.Symbols));
        return matchers;
    }
}
