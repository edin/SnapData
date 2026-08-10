namespace SnapData;

public static class Exp
{
    public static ColumnExpression Col(string name) => new(ColumnReference.Parse(name));

    public static ColumnExpression Col(ColumnReference column) => new(column);

    public static RawPredicate Raw(string sql) => new(sql);

    public static RawValueExpression RawValue(string sql) => new(sql);

    public static PredicateExpression Exists(SelectQueryBuilder query) =>
        new ExistsPredicate(query, false);

    public static PredicateExpression NotExists(SelectQueryBuilder query) =>
        new ExistsPredicate(query, true);

    public static PredicateExpression Not(PredicateExpression predicate) =>
        new NotPredicate(predicate);
}

public enum AggregateFunction
{
    Count,
    Sum,
    Average,
    Minimum,
    Maximum
}

public sealed class AggregateExpression : ValueExpression, ISelectExpression
{
    internal AggregateExpression(
        AggregateFunction function,
        ColumnReference column,
        bool distinct = false,
        string? alias = null)
    {
        ArgumentNullException.ThrowIfNull(column);
        Function = function;
        Column = column;
        Distinct = distinct;
        Alias = alias;
    }

    public AggregateFunction Function { get; }

    public ColumnReference Column { get; }

    public bool Distinct { get; }

    public string? Alias { get; }

    public AggregateExpression As(string alias)
    {
        TableReference.ValidatePart(alias, nameof(alias));
        return new AggregateExpression(Function, Column, Distinct, alias);
    }

    public AggregateExpression DistinctValues() =>
        new(Function, Column, true, Alias);

    public static PredicateExpression operator ==(AggregateExpression aggregate, object? value) =>
        new ComparisonPredicate(aggregate, ComparisonOperator.Equal, value);

    public static PredicateExpression operator !=(AggregateExpression aggregate, object? value) =>
        new ComparisonPredicate(aggregate, ComparisonOperator.NotEqual, value);

    public static PredicateExpression operator >(AggregateExpression aggregate, object? value) =>
        new ComparisonPredicate(aggregate, ComparisonOperator.GreaterThan, value);

    public static PredicateExpression operator <(AggregateExpression aggregate, object? value) =>
        new ComparisonPredicate(aggregate, ComparisonOperator.LessThan, value);

    public static PredicateExpression operator >=(AggregateExpression aggregate, object? value) =>
        new ComparisonPredicate(aggregate, ComparisonOperator.GreaterThanOrEqual, value);

    public static PredicateExpression operator <=(AggregateExpression aggregate, object? value) =>
        new ComparisonPredicate(aggregate, ComparisonOperator.LessThanOrEqual, value);

    public override bool Equals(object? obj) =>
        obj is AggregateExpression other
        && Function == other.Function
        && Column == other.Column
        && Distinct == other.Distinct
        && Alias == other.Alias;

    public override int GetHashCode() => HashCode.Combine(Function, Column, Distinct, Alias);
}

public abstract class SqlExpression;

public abstract class ValueExpression : SqlExpression;

public sealed class ColumnExpression : ValueExpression
{
    internal ColumnExpression(ColumnReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Alias is not null)
        {
            throw new ArgumentException(
                "Column aliases are not valid inside value or predicate expressions.",
                nameof(reference));
        }

        Reference = new ColumnReference(reference.Name, reference.Qualifier);
    }

    public ColumnReference Reference { get; }

    public string Name => Reference.Name;

    public string? Qualifier => Reference.Qualifier;

    public ColumnReference As(string alias) => Reference.As(alias);

    public static implicit operator ColumnReference(ColumnExpression column) =>
        column.Reference;

    public PredicateExpression IsNull() => new NullPredicate(this, false);

    public PredicateExpression IsNotNull() => new NullPredicate(this, true);

    public PredicateExpression Like(object? value) =>
        new ComparisonPredicate(this, ComparisonOperator.Like, value);

    public PredicateExpression NotLike(object? value) =>
        new NotPredicate(Like(value));

    public PredicateExpression StartsWith(string value) => Like($"{value}%");

    public PredicateExpression EndsWith(string value) => Like($"%{value}");

    public PredicateExpression Contains(string value) => Like($"%{value}%");

    public PredicateExpression In(params object?[] values) =>
        new InPredicate(this, values);

    public PredicateExpression In(IEnumerable<object?> values) =>
        new InPredicate(this, [.. values]);

    public PredicateExpression In(SelectQueryBuilder query) =>
        new InSubqueryPredicate(this, query, false);

    public PredicateExpression NotIn(params object?[] values) =>
        new NotPredicate(In(values));

    public PredicateExpression NotIn(IEnumerable<object?> values) =>
        new NotPredicate(In(values));

    public PredicateExpression NotIn(SelectQueryBuilder query) =>
        new InSubqueryPredicate(this, query, true);

    public PredicateExpression Between(object? minimum, object? maximum) =>
        new BetweenPredicate(this, minimum, maximum);

    public PredicateExpression NotBetween(object? minimum, object? maximum) =>
        new NotPredicate(Between(minimum, maximum));

    public static PredicateExpression operator ==(ColumnExpression column, object? value) =>
        value is null
            ? new NullPredicate(column, false)
            : new ComparisonPredicate(column, ComparisonOperator.Equal, value);

    public static PredicateExpression operator !=(ColumnExpression column, object? value) =>
        value is null
            ? new NullPredicate(column, true)
            : new ComparisonPredicate(column, ComparisonOperator.NotEqual, value);

    public static PredicateExpression operator >(ColumnExpression column, object? value) =>
        new ComparisonPredicate(column, ComparisonOperator.GreaterThan, value);

    public static PredicateExpression operator <(ColumnExpression column, object? value) =>
        new ComparisonPredicate(column, ComparisonOperator.LessThan, value);

    public static PredicateExpression operator >=(ColumnExpression column, object? value) =>
        new ComparisonPredicate(column, ComparisonOperator.GreaterThanOrEqual, value);

    public static PredicateExpression operator <=(ColumnExpression column, object? value) =>
        new ComparisonPredicate(column, ComparisonOperator.LessThanOrEqual, value);

    public static ValueExpression operator +(ColumnExpression column, object? value) =>
        new BinaryValueExpression(column, ArithmeticOperator.Add, ToValue(value));

    public static ValueExpression operator -(ColumnExpression column, object? value) =>
        new BinaryValueExpression(column, ArithmeticOperator.Subtract, ToValue(value));

    public static ValueExpression operator *(ColumnExpression column, object? value) =>
        new BinaryValueExpression(column, ArithmeticOperator.Multiply, ToValue(value));

    public static ValueExpression operator /(ColumnExpression column, object? value) =>
        new BinaryValueExpression(column, ArithmeticOperator.Divide, ToValue(value));

    private static ValueExpression ToValue(object? value) =>
        value as ValueExpression ?? new ParameterValueExpression(value);

    public override bool Equals(object? obj) =>
        obj is ColumnExpression other && Reference.Equals(other.Reference);

    public override int GetHashCode() => Reference.GetHashCode();
}

public abstract class PredicateExpression : SqlExpression
{
    public PredicateExpression Not() => new NotPredicate(this);

    public static PredicateExpression operator &(PredicateExpression left, PredicateExpression right) =>
        new LogicalPredicate(left, LogicalOperator.And, right);

    public static PredicateExpression operator |(PredicateExpression left, PredicateExpression right) =>
        new LogicalPredicate(left, LogicalOperator.Or, right);
}

public sealed class RawPredicate : PredicateExpression
{
    internal RawPredicate(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        Sql = sql;
    }

    public string Sql { get; }
}

public sealed class RawValueExpression : ValueExpression
{
    internal RawValueExpression(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        Sql = sql;
    }

    public string Sql { get; }
}

internal sealed class ParameterValueExpression(object? value) : ValueExpression
{
    internal object? Value { get; } = value;
}

internal sealed class BinaryValueExpression(
    ValueExpression left,
    ArithmeticOperator @operator,
    ValueExpression right) : ValueExpression
{
    internal ValueExpression Left { get; } = left;
    internal ArithmeticOperator Operator { get; } = @operator;
    internal ValueExpression Right { get; } = right;
}

internal sealed class ComparisonPredicate(
    ValueExpression expression,
    ComparisonOperator @operator,
    object? value) : PredicateExpression
{
    internal ValueExpression Expression { get; } = expression;
    internal ComparisonOperator Operator { get; } = @operator;
    internal object? Value { get; } = value;
}

internal sealed class LogicalPredicate(
    PredicateExpression left,
    LogicalOperator @operator,
    PredicateExpression right) : PredicateExpression
{
    internal PredicateExpression Left { get; } = left;
    internal LogicalOperator Operator { get; } = @operator;
    internal PredicateExpression Right { get; } = right;
}

internal sealed class NotPredicate(PredicateExpression operand) : PredicateExpression
{
    internal PredicateExpression Operand { get; } = operand;
}

internal sealed class NullPredicate(ColumnExpression column, bool negated) : PredicateExpression
{
    internal ColumnExpression Column { get; } = column;
    internal bool Negated { get; } = negated;
}

internal sealed class InPredicate(
    ColumnExpression column,
    IReadOnlyList<object?> values) : PredicateExpression
{
    internal ColumnExpression Column { get; } = column;
    internal IReadOnlyList<object?> Values { get; } = values;
}

internal sealed class BetweenPredicate(
    ColumnExpression column,
    object? minimum,
    object? maximum) : PredicateExpression
{
    internal ColumnExpression Column { get; } = column;
    internal object? Minimum { get; } = minimum;
    internal object? Maximum { get; } = maximum;
}

internal sealed class ExistsPredicate(SelectQueryBuilder query, bool negated) : PredicateExpression
{
    internal SelectQueryBuilder Query { get; } = query
        ?? throw new ArgumentNullException(nameof(query));
    internal bool Negated { get; } = negated;
}

internal sealed class InSubqueryPredicate(
    ColumnExpression column,
    SelectQueryBuilder query,
    bool negated) : PredicateExpression
{
    internal ColumnExpression Column { get; } = column;
    internal SelectQueryBuilder Query { get; } = query
        ?? throw new ArgumentNullException(nameof(query));
    internal bool Negated { get; } = negated;
}

internal enum ComparisonOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    Like
}

internal enum LogicalOperator
{
    And,
    Or
}

internal enum ArithmeticOperator
{
    Add,
    Subtract,
    Multiply,
    Divide
}
