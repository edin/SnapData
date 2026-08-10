using System.Data;
using System.Collections.Concurrent;
using System.Reflection;

namespace SnapData;

public interface IStoredProc<TResult>;

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
        var attribute = requestType.GetCustomAttribute<StoredProcedureAttribute>()
            ?? throw new InvalidOperationException(
                $"Stored procedure request {requestType.Name} requires {nameof(StoredProcedureAttribute)}.");

        return new CommandDefinition(
            attribute.Name,
            ParameterSet.From(procedure),
            CommandType.StoredProcedure);
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
