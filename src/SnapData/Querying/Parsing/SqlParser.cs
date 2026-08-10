using System.Globalization;

namespace SnapData;

public static class SqlParser
{
    public static PredicateExpression ParseCriteria(
        string criteria,
        object? parameters = null) =>
        Parser.Create(criteria, parameters).ParseCriteria();

    public static ParsedJoin ParseJoin(
        string clause,
        object? parameters = null) =>
        Parser.Create(clause, parameters).ParseJoin();

    private sealed class Parser(
        CriteriaTokenStream tokens,
        ParameterSet parameters)
    {
        internal static Parser Create(string source, object? parameters)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            var scan = new CriteriaScanner(source).Scan();
            if (!scan.Succeeded)
            {
                throw SqlParseException.FromDiagnostic(scan.Diagnostics[0]);
            }

            return new Parser(new CriteriaTokenStream(scan.Tokens), ParameterSet.From(parameters));
        }

        internal PredicateExpression ParseCriteria()
        {
            var predicate = ParseOr();
            RequireEnd();
            return predicate;
        }

        internal ParsedJoin ParseJoin()
        {
            var table = ParseTable();
            tokens.Require(CriteriaTokenKind.On, "Expected ON after the joined table reference.");
            var predicate = ParseOr();
            RequireEnd();
            return new ParsedJoin(table, predicate);
        }

        private PredicateExpression ParseOr()
        {
            var predicate = ParseAnd();
            while (tokens.Match(CriteriaTokenKind.Or) is not null)
            {
                predicate |= ParseAnd();
            }

            return predicate;
        }

        private PredicateExpression ParseAnd()
        {
            var predicate = ParseNot();
            while (tokens.Match(CriteriaTokenKind.And) is not null)
            {
                predicate &= ParseNot();
            }

            return predicate;
        }

        private PredicateExpression ParseNot()
        {
            if (tokens.Match(CriteriaTokenKind.Not) is not null)
            {
                return new NotPredicate(ParseNot());
            }

            if (tokens.Match(CriteriaTokenKind.LeftParenthesis) is not null)
            {
                var predicate = ParseOr();
                tokens.Require(CriteriaTokenKind.RightParenthesis, "Expected ')' after criteria.");
                return predicate;
            }

            return ParseComparison();
        }

        private PredicateExpression ParseComparison()
        {
            var column = ParseColumnExpression();
            if (tokens.Match(CriteriaTokenKind.Is) is not null)
            {
                var negated = tokens.Match(CriteriaTokenKind.Not) is not null;
                tokens.Require(CriteriaTokenKind.Null, "Expected NULL after IS or IS NOT.");
                return new NullPredicate(column, negated);
            }

            var negatesOperator = tokens.Match(CriteriaTokenKind.Not) is not null;
            PredicateExpression? predicate = null;
            if (tokens.Match(CriteriaTokenKind.In) is not null)
            {
                predicate = ParseIn(column);
            }
            else if (tokens.Match(CriteriaTokenKind.Between) is not null)
            {
                var minimum = ParseValue();
                tokens.Require(CriteriaTokenKind.And, "Expected AND in BETWEEN criteria.");
                predicate = new BetweenPredicate(column, minimum, ParseValue());
            }
            else if (tokens.Match(CriteriaTokenKind.Like) is not null)
            {
                predicate = new ComparisonPredicate(
                    column,
                    ComparisonOperator.Like,
                    ParseValue());
            }
            else if (negatesOperator)
            {
                throw SqlParseException.At(
                    tokens.Current,
                    "Expected IN, BETWEEN, or LIKE after NOT.");
            }

            if (predicate is not null)
            {
                return negatesOperator ? new NotPredicate(predicate) : predicate;
            }

            var operation = tokens.Current.Kind switch
            {
                CriteriaTokenKind.Equal => ComparisonOperator.Equal,
                CriteriaTokenKind.NotEqual or CriteriaTokenKind.NotEqualSql =>
                    ComparisonOperator.NotEqual,
                CriteriaTokenKind.LessThan => ComparisonOperator.LessThan,
                CriteriaTokenKind.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
                CriteriaTokenKind.GreaterThan => ComparisonOperator.GreaterThan,
                CriteriaTokenKind.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
                _ => (ComparisonOperator?)null
            };
            if (operation is null)
            {
                return new ComparisonPredicate(column, ComparisonOperator.Equal, true);
            }

            tokens.Advance();
            return Compare(column, operation.Value, ParseValue());
        }

        private PredicateExpression ParseIn(ColumnExpression column)
        {
            tokens.Require(CriteriaTokenKind.LeftParenthesis, "Expected '(' after IN.");
            var values = new List<object?>();
            if (!tokens.Check(CriteriaTokenKind.RightParenthesis))
            {
                do
                {
                    values.Add(ParseValue());
                }
                while (tokens.Match(CriteriaTokenKind.Comma) is not null);
            }

            tokens.Require(CriteriaTokenKind.RightParenthesis, "Expected ')' after IN values.");
            return new InPredicate(column, values);
        }

        private object? ParseValue()
        {
            if (tokens.Match(CriteriaTokenKind.Parameter) is { } parameter)
            {
                if (!parameters.TryGetValue(parameter.Value, out var value))
                {
                    throw SqlParseException.At(
                        parameter,
                        $"No value was supplied for parameter '{parameter.Value}'.");
                }

                return value;
            }

            if (tokens.Match(CriteriaTokenKind.String) is { } text)
            {
                return text.Value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            if (tokens.Match(CriteriaTokenKind.Number) is { } number)
            {
                if (long.TryParse(number.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    return integer;
                }

                if (decimal.TryParse(number.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
                {
                    return decimalValue;
                }

                if (double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
                {
                    return floating;
                }

                throw SqlParseException.At(number, $"Invalid numeric literal '{number.Value}'.");
            }

            if (tokens.Match(CriteriaTokenKind.True) is not null)
            {
                return true;
            }

            if (tokens.Match(CriteriaTokenKind.False) is not null)
            {
                return false;
            }

            if (tokens.Match(CriteriaTokenKind.Null) is not null)
            {
                return null;
            }

            if (tokens.Check(CriteriaTokenKind.Identifier)
                || tokens.Check(CriteriaTokenKind.QuotedIdentifier))
            {
                return ParseColumnExpression();
            }

            throw SqlParseException.At(tokens.Current, "Expected a column, parameter, or literal value.");
        }

        private ColumnExpression ParseColumnExpression() => Exp.Col(ParseColumnReference());

        private ColumnReference ParseColumnReference()
        {
            var first = ParseIdentifier("Expected a column name.");
            return tokens.Match(CriteriaTokenKind.Dot) is null
                ? new ColumnReference(first)
                : new ColumnReference(ParseIdentifier("Expected a column name after '.'."), first);
        }

        private TableReference ParseTable()
        {
            var first = ParseIdentifier("Expected a table name.");
            string? schema = null;
            string name;
            if (tokens.Match(CriteriaTokenKind.Dot) is not null)
            {
                schema = first;
                name = ParseIdentifier("Expected a table name after '.'.");
            }
            else
            {
                name = first;
            }

            string? alias = null;
            if (tokens.Match(CriteriaTokenKind.As) is not null)
            {
                alias = ParseIdentifier("Expected an alias after AS.");
            }
            else if (tokens.Check(CriteriaTokenKind.Identifier)
                || tokens.Check(CriteriaTokenKind.QuotedIdentifier))
            {
                alias = ParseIdentifier("Expected a table alias.");
            }

            return new TableReference(name, schema, alias);
        }

        private string ParseIdentifier(string message)
        {
            if (tokens.Match(CriteriaTokenKind.Identifier) is { } identifier)
            {
                return identifier.Value;
            }

            if (tokens.Match(CriteriaTokenKind.QuotedIdentifier) is { } quoted)
            {
                var close = quoted.Value[^1];
                return quoted.Value[1..^1].Replace(
                    new string(close, 2),
                    close.ToString(),
                    StringComparison.Ordinal);
            }

            throw SqlParseException.At(tokens.Current, message);
        }

        private void RequireEnd()
        {
            if (!tokens.IsAtEnd)
            {
                throw SqlParseException.At(tokens.Current, $"Unexpected token '{tokens.Current.Value}'.");
            }
        }

        private static PredicateExpression Compare(
            ColumnExpression column,
            ComparisonOperator operation,
            object? value) =>
            operation switch
            {
                ComparisonOperator.Equal => column == value,
                ComparisonOperator.NotEqual => column != value,
                ComparisonOperator.GreaterThan => column > value,
                ComparisonOperator.LessThan => column < value,
                ComparisonOperator.GreaterThanOrEqual => column >= value,
                ComparisonOperator.LessThanOrEqual => column <= value,
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
    }
}

public sealed record ParsedJoin(TableReference Table, PredicateExpression Predicate);

public sealed class SqlParseException : FormatException
{
    private SqlParseException(string message, int position, int line, int column)
        : base($"{message} At line {line}, column {column}.")
    {
        Position = position;
        Line = line;
        Column = column;
    }

    public int Position { get; }

    public int Line { get; }

    public int Column { get; }

    internal static SqlParseException At(CriteriaToken token, string message) =>
        new(message, token.Span.Position, token.Span.Line, token.Span.Column);

    internal static SqlParseException FromDiagnostic(CriteriaDiagnostic diagnostic) =>
        new(
            diagnostic.Message,
            diagnostic.Span.Position,
            diagnostic.Span.Line,
            diagnostic.Span.Column);
}
