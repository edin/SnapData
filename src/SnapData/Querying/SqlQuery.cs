namespace SnapData;

public sealed class SqlQuery : CommandDefinition
{
    public SqlQuery(string text, IReadOnlyDictionary<string, object?>? parameters = null)
        : base(text, ParameterSet.From(parameters))
    {
    }

    internal SqlQuery(string text, ParameterSet parameters) : base(text, parameters)
    {
    }

    public string Text => CommandText;
}
