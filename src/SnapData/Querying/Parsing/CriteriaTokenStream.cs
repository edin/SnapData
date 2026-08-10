namespace SnapData;

internal sealed class CriteriaTokenStream(IReadOnlyList<CriteriaToken> tokens)
{
    private int _position;

    internal CriteriaToken Current => At(_position);

    internal CriteriaToken Previous => At(_position - 1);

    internal bool IsAtEnd => Current.Kind == CriteriaTokenKind.EndOfFile;

    internal CriteriaToken Advance()
    {
        var token = Current;
        if (!IsAtEnd)
        {
            _position++;
        }

        return token;
    }

    internal bool Check(CriteriaTokenKind kind) => Current.Kind == kind;

    internal CriteriaToken? Match(CriteriaTokenKind kind) =>
        Check(kind) ? Advance() : null;

    internal CriteriaToken Require(CriteriaTokenKind kind, string message) =>
        Match(kind) ?? throw SqlParseException.At(Current, message);

    private CriteriaToken At(int position)
    {
        if (position < 0)
        {
            return tokens[0];
        }

        return position < tokens.Count ? tokens[position] : tokens[^1];
    }
}
