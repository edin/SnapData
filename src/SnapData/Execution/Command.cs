using System.Data;

namespace SnapData;

public sealed class Command : CommandDefinition
{
    private Command(
        string commandText,
        ParameterSet? parameters,
        CommandType commandType)
        : base(commandText, parameters, commandType)
    {
    }

    public static Command Text(string sql, ParameterSet? parameters = null) =>
        new(sql, parameters, CommandType.Text);

    public static Command StoredProcedure(
        string name,
        ParameterSet? parameters = null) =>
        new(name, parameters, CommandType.StoredProcedure);

    public Command Input(
        string name,
        object? value,
        DbType? dbType = null,
        int? size = null)
    {
        Parameters.Input(name, value, dbType, size);
        return this;
    }

    public ParameterReference<T> Output<T>(
        string name,
        DbType? dbType = null,
        int? size = null)
    {
        Parameters.Output<T>(name, dbType, size);
        return new ParameterReference<T>(Parameters, name);
    }

    public ParameterReference<T> InputOutput<T>(
        string name,
        T? value,
        DbType? dbType = null,
        int? size = null)
    {
        Parameters.InputOutput(name, value, dbType, size);
        return new ParameterReference<T>(Parameters, name);
    }

    public ParameterReference<T> ReturnValue<T>(
        string name = "return_value",
        DbType? dbType = null)
    {
        Parameters.ReturnValue<T>(name, dbType);
        return new ParameterReference<T>(Parameters, name);
    }

    public Command AddParameter(CommandParameter parameter)
    {
        Parameters.Add(parameter);
        return this;
    }

    public Command SetParameter(string name, object? value)
    {
        Parameters.GetParameter(name).SetValue(value);
        return this;
    }

    public ParameterReference<T> Parameter<T>(string name) =>
        new(Parameters, name);
}

public sealed class ParameterReference<T>
{
    private readonly ParameterSet _parameters;

    internal ParameterReference(ParameterSet parameters, string name)
    {
        _ = parameters.GetParameter(name);
        _parameters = parameters;
        Name = name;
    }

    public string Name { get; }

    public T? Value
    {
        get => _parameters.Get<T>(Name);
        set => Parameter.SetValue(value);
    }

    public CommandParameter Parameter => _parameters.GetParameter(Name);
}
