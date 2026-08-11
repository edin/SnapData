using System.Linq.Expressions;

namespace SnapData;

public sealed class EntityReference<T> where T : class
{
    private readonly EntityMapping _mapping;

    internal EntityReference(EntityMapping mapping, string? alias)
    {
        _mapping = mapping;
        Table = alias is null ? mapping.Table : mapping.Table.As(alias);
    }

    public TableReference Table { get; }

    public ColumnExpression Col<TValue>(Expression<Func<T, TValue>> property) =>
        Exp.Col(ResolveColumn(property));

    public ColumnReference Col<TValue>(
        Expression<Func<T, TValue>> property,
        string alias) =>
        ResolveColumn(property).As(alias);

    private ColumnReference ResolveColumn<TValue>(Expression<Func<T, TValue>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        Expression body = property.Body;
        while (body is UnaryExpression unary
            && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            body = unary.Operand;
        }

        if (body is not MemberExpression member || member.Expression != property.Parameters[0])
        {
            throw new ArgumentException(
                "Column selection requires a direct mapped property expression.",
                nameof(property));
        }

        var mapping = _mapping.FindProperty(member.Member.Name)
            ?? throw new InvalidOperationException(
                $"Property {typeof(T).Name}.{member.Member.Name} is not mapped as a column.");
        return mapping.Column.Qualify(Table.Alias ?? Table.Name);
    }
}

public sealed record TableReference
{
    public TableReference(string name, string? schema = null, string? alias = null)
    {
        ValidatePart(name, nameof(name));
        ValidateOptionalPart(schema, nameof(schema));
        ValidateOptionalPart(alias, nameof(alias));
        Name = name;
        Schema = schema;
        Alias = alias;
    }

    public string Name { get; }

    public string? Schema { get; }

    public string? Alias { get; }

    public TableReference As(string alias) => new(Name, Schema, alias);

    public ColumnExpression Col(string name) =>
        Exp.Col(ColumnReference.Parse(name).Qualify(Alias ?? Name));

    public static TableReference Parse(string value)
    {
        var (reference, alias) = ReferenceParser.SplitAlias(value);
        var parts = reference.Split('.');
        return parts.Length switch
        {
            1 => new TableReference(parts[0], alias: alias),
            2 => new TableReference(parts[1], parts[0], alias),
            _ => throw ReferenceParser.Invalid("table", value)
        };
    }

    internal static void ValidatePart(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(char.IsWhiteSpace) || value.Contains('.'))
        {
            throw new ArgumentException(
                $"Identifier part '{value}' cannot contain whitespace or dots.",
                parameterName);
        }
    }

    private static void ValidateOptionalPart(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidatePart(value, parameterName);
        }
    }
}

public interface ISelectExpression;

public sealed record ColumnReference : ISelectExpression
{
    public ColumnReference(string name, string? qualifier = null, string? alias = null)
    {
        if (name != "*")
        {
            TableReference.ValidatePart(name, nameof(name));
        }

        if (qualifier is not null)
        {
            TableReference.ValidatePart(qualifier, nameof(qualifier));
        }

        if (alias is not null)
        {
            TableReference.ValidatePart(alias, nameof(alias));
        }

        Name = name;
        Qualifier = qualifier;
        Alias = alias;
    }

    public string Name { get; }

    public string? Qualifier { get; }

    public string? Alias { get; }

    public ColumnReference As(string alias) => new(Name, Qualifier, alias);

    internal ColumnReference Qualify(string qualifier) =>
        Qualifier is null ? new(Name, qualifier, Alias) : this;

    public static ColumnReference Parse(string value)
    {
        var (reference, alias) = ReferenceParser.SplitAlias(value);
        var parts = reference.Split('.');
        return parts.Length switch
        {
            1 => new ColumnReference(parts[0], alias: alias),
            2 => new ColumnReference(parts[1], parts[0], alias),
            _ => throw ReferenceParser.Invalid("column", value)
        };
    }
}

internal static class ReferenceParser
{
    internal static (string Reference, string? Alias) SplitAlias(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => (parts[0], null),
            2 when !parts[1].Equals("AS", StringComparison.OrdinalIgnoreCase) =>
                (parts[0], parts[1]),
            3 when parts[1].Equals("AS", StringComparison.OrdinalIgnoreCase) =>
                (parts[0], parts[2]),
            _ => throw Invalid("reference", value)
        };
    }

    internal static ArgumentException Invalid(string kind, string value) =>
        new($"Invalid {kind} reference '{value}'. Use a structured reference or a simple qualified identifier with an optional alias.");
}
