using System.Data;

namespace SnapData;

public class CommandDefinition
{
    public CommandDefinition(
        string commandText,
        ParameterSet? parameters = null,
        CommandType commandType = CommandType.Text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        CommandText = commandText;
        Parameters = parameters ?? ParameterSet.Empty;
        CommandType = commandType;
    }

    public string CommandText { get; }

    public ParameterSet Parameters { get; }

    public CommandType CommandType { get; }

    public override string ToString() => CommandText;
}
