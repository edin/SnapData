using System.Collections;
using System.Reflection;

namespace SnapData;

internal static class ParameterReader
{
    internal static IEnumerable<KeyValuePair<string, object?>> Read(object? parameters)
    {
        if (parameters is null)
        {
            yield break;
        }

        if (parameters is IEnumerable<KeyValuePair<string, object?>> typed)
        {
            foreach (var pair in typed)
            {
                yield return pair;
            }

            yield break;
        }

        if (parameters is IDictionary dictionary)
        {
            foreach (DictionaryEntry pair in dictionary)
            {
                yield return new KeyValuePair<string, object?>(
                    Convert.ToString(pair.Key, System.Globalization.CultureInfo.InvariantCulture)
                        ?? throw new ArgumentException("Parameter names cannot be null."),
                    pair.Value);
            }

            yield break;
        }

        foreach (var property in parameters.GetType().GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
            {
                yield return new KeyValuePair<string, object?>(
                    property.Name,
                    property.GetValue(parameters));
            }
        }
    }
}
