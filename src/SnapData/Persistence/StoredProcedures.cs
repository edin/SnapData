using System.Data;
using System.Collections.Concurrent;
using System.Reflection;

namespace SnapData;

public interface IStoredProc<TResult>;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public abstract class StoredProcedureParameterAttribute : Attribute
{
    protected StoredProcedureParameterAttribute()
    {
    }

    protected StoredProcedureParameterAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string? Name { get; }

    public DbType DbType { get; set; } = DbType.Object;

    public int Size { get; set; } = -1;

    public byte Precision { get; set; }

    public byte Scale { get; set; }
}

public sealed class InputAttribute : StoredProcedureParameterAttribute
{
    public InputAttribute()
    {
    }

    public InputAttribute(string name) : base(name)
    {
    }
}

public sealed class OutputAttribute : StoredProcedureParameterAttribute
{
    public OutputAttribute()
    {
    }

    public OutputAttribute(string name) : base(name)
    {
    }
}

public sealed class InputOutputAttribute : StoredProcedureParameterAttribute
{
    public InputOutputAttribute()
    {
    }

    public InputOutputAttribute(string name) : base(name)
    {
    }
}

public sealed class ReturnValueAttribute : StoredProcedureParameterAttribute
{
    public ReturnValueAttribute()
    {
    }

    public ReturnValueAttribute(string name) : base(name)
    {
    }
}

public sealed class Result<T>
{
    public Result()
    {
    }

    public Result(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items.AddRange(items);
    }

    public List<T> Items { get; } = [];
}

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ResultSetAttribute(int index) : Attribute
{
    public int Index { get; } = index >= 0
        ? index
        : throw new ArgumentOutOfRangeException(nameof(index));
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StoredProcedureAttribute : Attribute
{
    public StoredProcedureAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
}

internal static class StoredProcedureCommandFactory
{
    internal static CommandDefinition Create<TResult>(IStoredProc<TResult> procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        var requestType = procedure.GetType();
        var procedureAttribute = requestType.GetCustomAttribute<StoredProcedureAttribute>()
            ?? throw new InvalidOperationException(
                $"Stored procedure request {requestType.Name} requires {nameof(StoredProcedureAttribute)}.");

        var parameters = new ParameterSet();
        foreach (var property in RequestProperties(requestType))
        {
            var parameterAttribute = GetParameterAttribute(property);
            var direction = Direction(parameterAttribute);
            EnsurePropertyAccess(requestType, property, direction);
            var name = ParameterName(property, parameterAttribute, direction);
            var value = direction is ParameterDirection.Input or ParameterDirection.InputOutput
                ? property.GetValue(procedure)
                : null;

            if (parameterAttribute is null)
            {
                parameters.Input(name, value);
                continue;
            }

            parameters.Add(new CommandParameter(
                name,
                value,
                direction,
                parameterAttribute.DbType == DbType.Object
                    ? ParameterSet.InferDbType(property.PropertyType)
                    : parameterAttribute.DbType,
                parameterAttribute.Size >= 0 ? parameterAttribute.Size : null,
                parameterAttribute.Precision > 0 ? parameterAttribute.Precision : null,
                parameterAttribute.Scale > 0 ? parameterAttribute.Scale : null));
        }

        return Command.StoredProcedure(procedureAttribute.Name, parameters);
    }

    internal static void ApplyOutputs<TResult>(
        IStoredProc<TResult> procedure,
        ParameterSet parameters)
    {
        foreach (var property in RequestProperties(procedure.GetType()))
        {
            var attribute = GetParameterAttribute(property);
            var direction = Direction(attribute);
            if (direction == ParameterDirection.Input)
            {
                continue;
            }

            var value = parameters[ParameterName(property, attribute, direction)];
            property.SetValue(
                procedure,
                value is null
                    ? null
                    : RowMapper<TResult>.ConvertValue(value, property.PropertyType));
        }
    }

    private static IEnumerable<PropertyInfo> RequestProperties(Type requestType) =>
        requestType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0);

    private static StoredProcedureParameterAttribute? GetParameterAttribute(
        PropertyInfo property)
    {
        var attributes = property
            .GetCustomAttributes<StoredProcedureParameterAttribute>(inherit: true)
            .ToArray();
        return attributes.Length switch
        {
            0 => null,
            1 => attributes[0],
            _ => throw new InvalidOperationException(
                $"Stored-procedure property {property.DeclaringType?.Name}.{property.Name} has multiple parameter-direction attributes.")
        };
    }

    private static ParameterDirection Direction(StoredProcedureParameterAttribute? attribute) =>
        attribute switch
        {
            OutputAttribute => ParameterDirection.Output,
            InputOutputAttribute => ParameterDirection.InputOutput,
            ReturnValueAttribute => ParameterDirection.ReturnValue,
            _ => ParameterDirection.Input
        };

    private static string ParameterName(
        PropertyInfo property,
        StoredProcedureParameterAttribute? attribute,
        ParameterDirection direction) =>
        attribute?.Name
        ?? (direction == ParameterDirection.ReturnValue ? "return_value" : property.Name);

    private static void EnsurePropertyAccess(
        Type requestType,
        PropertyInfo property,
        ParameterDirection direction)
    {
        if (direction is ParameterDirection.Input or ParameterDirection.InputOutput
            && !property.CanRead)
        {
            throw new InvalidOperationException(
                $"Input property {requestType.Name}.{property.Name} must be readable.");
        }

        if (direction is ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue
            && !property.CanWrite)
        {
            throw new InvalidOperationException(
                $"Output property {requestType.Name}.{property.Name} must be writable.");
        }
    }
}

internal static class StoredProcedureResultMappingProvider
{
    private static readonly ConcurrentDictionary<Type, StoredProcedureResultMapping> Cache = new();

    internal static StoredProcedureResultMapping Get<TResult>() =>
        Cache.GetOrAdd(typeof(TResult), Build);

    private static StoredProcedureResultMapping Build(Type resultType)
    {
        if (!resultType.IsClass || resultType.IsAbstract)
        {
            throw new InvalidOperationException(
                $"Stored procedure result {resultType.Name} must be a concrete class.");
        }

        var properties = resultType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Select(property => new
            {
                Property = property,
                ItemType = GetListItemType(property.PropertyType),
                Attribute = property.GetCustomAttribute<ResultSetAttribute>(inherit: true)
            })
            .Where(candidate => candidate.ItemType is not null)
            .OrderBy(candidate => candidate.Property.MetadataToken)
            .ToArray();
        if (properties.Length == 0)
        {
            throw new InvalidOperationException(
                $"Stored procedure result {resultType.Name} requires at least one public List<T> property.");
        }

        var attributedCount = properties.Count(candidate => candidate.Attribute is not null);
        if (attributedCount > 0 && attributedCount != properties.Length)
        {
            throw new InvalidOperationException(
                $"Stored procedure result {resultType.Name} must either annotate every List<T> property with {nameof(ResultSetAttribute)} or use declaration order for all of them.");
        }

        var resultSets = attributedCount == 0
            ? properties.Select((candidate, index) => new StoredProcedureResultSetMapping(
                index,
                candidate.Property,
                candidate.ItemType!)).ToArray()
            : properties.Select(candidate => new StoredProcedureResultSetMapping(
                    candidate.Attribute!.Index,
                    candidate.Property,
                    candidate.ItemType!))
                .OrderBy(mapping => mapping.Index)
                .ToArray();
        var expectedIndices = Enumerable.Range(0, resultSets.Length);
        if (!resultSets.Select(mapping => mapping.Index).SequenceEqual(expectedIndices))
        {
            throw new InvalidOperationException(
                $"Stored procedure result {resultType.Name} result-set indices must be unique and contiguous starting at zero.");
        }

        return new StoredProcedureResultMapping(resultType, resultSets);
    }

    private static Type? GetListItemType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            ? type.GetGenericArguments()[0]
            : null;
}

internal sealed record StoredProcedureResultMapping(
    Type ResultType,
    IReadOnlyList<StoredProcedureResultSetMapping> ResultSets)
{
    internal object CreateInstance() =>
        Activator.CreateInstance(ResultType, nonPublic: true)
        ?? throw new InvalidOperationException(
            $"Stored procedure result {ResultType.Name} requires a parameterless constructor.");
}

internal sealed record StoredProcedureResultSetMapping(
    int Index,
    PropertyInfo Property,
    Type ItemType);
