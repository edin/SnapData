using System.Linq.Expressions;
using System.Reflection;

namespace SnapData;

internal sealed class EntityExpressionTranslator<T>(EntityMapping mapping, string? qualifier = null)
{
    internal PredicateExpression Translate(Expression<Func<T, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return TranslatePredicate(expression.Body, expression.Parameters[0]);
    }

    internal ColumnReference TranslateProperty<TValue>(Expression<Func<T, TValue>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = UnwrapConvert(expression.Body);
        if (body is MemberExpression member && member.Expression == expression.Parameters[0])
        {
            return Qualify(GetProperty(member).Column);
        }

        throw Unsupported(expression.Body, "A direct mapped property is required");
    }

    private PredicateExpression TranslatePredicate(Expression expression, ParameterExpression parameter)
    {
        expression = UnwrapConvert(expression);
        return expression switch
        {
            BinaryExpression binary when binary.NodeType is ExpressionType.AndAlso or ExpressionType.And =>
                new LogicalPredicate(
                    TranslatePredicate(binary.Left, parameter),
                    LogicalOperator.And,
                    TranslatePredicate(binary.Right, parameter)),
            BinaryExpression binary when binary.NodeType is ExpressionType.OrElse or ExpressionType.Or =>
                new LogicalPredicate(
                    TranslatePredicate(binary.Left, parameter),
                    LogicalOperator.Or,
                    TranslatePredicate(binary.Right, parameter)),
            BinaryExpression binary when IsComparison(binary.NodeType) =>
                TranslateComparison(binary, parameter),
            UnaryExpression { NodeType: ExpressionType.Not } unary =>
                new NotPredicate(TranslatePredicate(unary.Operand, parameter)),
            MemberExpression member when IsEntityProperty(member, parameter) && member.Type == typeof(bool) =>
                new ComparisonPredicate(Column(member), ComparisonOperator.Equal, true),
            ConstantExpression { Type: { } type, Value: bool value } when type == typeof(bool) =>
                value ? Exp.Raw("1 = 1") : Exp.Raw("1 = 0"),
            _ => throw Unsupported(expression, "Use a comparison, boolean property, &&, ||, or !")
        };
    }

    private PredicateExpression TranslateComparison(BinaryExpression binary, ParameterExpression parameter)
    {
        var left = TranslateOperand(binary.Left, parameter);
        var right = TranslateOperand(binary.Right, parameter);
        var operation = ToComparison(binary.NodeType);

        if (left is ColumnExpression column)
        {
            return Compare(column, operation, right);
        }

        if (right is ColumnExpression rightColumn)
        {
            return Compare(rightColumn, Reverse(operation), left);
        }

        throw Unsupported(binary, "At least one side of a comparison must be a mapped property");
    }

    private object? TranslateOperand(Expression expression, ParameterExpression parameter)
    {
        expression = UnwrapConvert(expression);
        if (expression is MemberExpression member && IsEntityProperty(member, parameter))
        {
            return Column(member);
        }

        if (ContainsParameter(expression, parameter))
        {
            throw Unsupported(expression, "Only direct mapped properties can reference the entity parameter");
        }

        return EvaluateValue(expression);
    }

    private static object? EvaluateValue(Expression expression)
    {
        expression = UnwrapConvert(expression);
        return expression switch
        {
            ConstantExpression constant => constant.Value,
            MemberExpression member => ReadMember(
                member.Expression is null
                    ? null
                    : EvaluateValue(member.Expression),
                member.Member),
            _ => throw UnsupportedValue(expression)
        };
    }

    private static object? ReadMember(object? instance, MemberInfo member) =>
        member switch
        {
            FieldInfo field => field.GetValue(instance),
            PropertyInfo property when property.GetIndexParameters().Length == 0 =>
                property.GetValue(instance),
            _ => throw UnsupportedValue(
                Expression.MakeMemberAccess(
                    instance is null ? null : Expression.Constant(instance),
                    member))
        };

    private static NotSupportedException UnsupportedValue(Expression expression) =>
        new(
            $"Unsupported captured value expression '{expression}'. "
            + "SnapData accepts constants and captured field/property access only. "
            + "Evaluate the expression before constructing the query.");

    private ColumnExpression Column(MemberExpression member) =>
        Exp.Col(Qualify(GetProperty(member).Column));

    private ColumnReference Qualify(ColumnReference column) =>
        qualifier is null ? column : column.Qualify(qualifier);

    private PropertyMapping GetProperty(MemberExpression member) =>
        mapping.FindProperty(member.Member.Name)
        ?? throw new NotSupportedException(
            $"Property '{mapping.EntityType.Name}.{member.Member.Name}' is not mapped.");

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

    private static ComparisonOperator ToComparison(ExpressionType type) =>
        type switch
        {
            ExpressionType.Equal => ComparisonOperator.Equal,
            ExpressionType.NotEqual => ComparisonOperator.NotEqual,
            ExpressionType.GreaterThan => ComparisonOperator.GreaterThan,
            ExpressionType.LessThan => ComparisonOperator.LessThan,
            ExpressionType.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
            ExpressionType.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private static ComparisonOperator Reverse(ComparisonOperator operation) =>
        operation switch
        {
            ComparisonOperator.GreaterThan => ComparisonOperator.LessThan,
            ComparisonOperator.LessThan => ComparisonOperator.GreaterThan,
            ComparisonOperator.GreaterThanOrEqual => ComparisonOperator.LessThanOrEqual,
            ComparisonOperator.LessThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
            _ => operation
        };

    private static bool IsComparison(ExpressionType type) =>
        type is ExpressionType.Equal
            or ExpressionType.NotEqual
            or ExpressionType.GreaterThan
            or ExpressionType.LessThan
            or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThanOrEqual;

    private static bool IsEntityProperty(MemberExpression member, ParameterExpression parameter) =>
        UnwrapConvert(member.Expression!) == parameter;

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression unary
            && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static bool ContainsParameter(Expression expression, ParameterExpression parameter)
    {
        var visitor = new ParameterFindingVisitor(parameter);
        visitor.Visit(expression);
        return visitor.Found;
    }

    private static NotSupportedException Unsupported(Expression expression, string detail) =>
        new($"Unsupported entity expression '{expression}': {detail}. Use SnapData Exp for advanced predicates.");

    private sealed class ParameterFindingVisitor(ParameterExpression parameter) : ExpressionVisitor
    {
        internal bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= node == parameter;
            return node;
        }
    }
}
