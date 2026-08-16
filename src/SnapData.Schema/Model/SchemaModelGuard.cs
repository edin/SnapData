namespace SnapData.Schema;

internal static class SchemaModelGuard
{
    internal static string RequiredName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    internal static IReadOnlyList<T> Snapshot<T>(IEnumerable<T>? items) =>
        Array.AsReadOnly(items?.ToArray() ?? []);

    internal static IReadOnlyList<string> RequiredNames(
        IEnumerable<string> names,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(names, parameterName);
        var snapshot = names.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("At least one column is required.", parameterName);
        }

        foreach (var name in snapshot)
        {
            RequiredName(name, parameterName);
        }

        return Array.AsReadOnly(snapshot);
    }
}
