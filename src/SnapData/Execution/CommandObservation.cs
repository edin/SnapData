using System.Data;
using System.Diagnostics;

namespace SnapData;

public interface ICommandObserver
{
    void Executing(CommandExecutingContext context);

    void Executed(CommandExecutedContext context);

    void Failed(CommandFailedContext context);
}

public abstract class CommandObserver : ICommandObserver
{
    public virtual void Executing(CommandExecutingContext context)
    {
    }

    public virtual void Executed(CommandExecutedContext context)
    {
    }

    public virtual void Failed(CommandFailedContext context)
    {
    }
}

public sealed record CommandExecutingContext(
    CommandSnapshot Command,
    CommandExecutionKind Kind,
    string ProviderName,
    bool HasTransaction);

public sealed record CommandExecutedContext(
    CommandSnapshot Command,
    CommandExecutionKind Kind,
    string ProviderName,
    bool HasTransaction,
    TimeSpan Duration,
    int? AffectedRows,
    int? ResultCount);

public sealed record CommandFailedContext(
    CommandSnapshot Command,
    CommandExecutionKind Kind,
    string ProviderName,
    bool HasTransaction,
    TimeSpan Duration,
    Exception Exception);

public enum CommandExecutionKind
{
    Query,
    Scalar,
    NonQuery
}

public sealed class CommandSnapshot
{
    private readonly IReadOnlyDictionary<string, CommandParameterSnapshot> _parametersByName;

    internal CommandSnapshot(CommandDefinition command)
    {
        CommandText = command.CommandText;
        CommandType = command.CommandType;
        Parameters = command.Parameters.Definitions
            .Select(parameter => new CommandParameterSnapshot(
                parameter.Name,
                parameter.Value,
                parameter.Direction,
                parameter.DbType,
                parameter.Size,
                parameter.Precision,
                parameter.Scale))
            .ToArray();
        _parametersByName = Parameters.ToDictionary(
            parameter => NormalizeName(parameter.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    public string CommandText { get; }

    public CommandType CommandType { get; }

    public IReadOnlyList<CommandParameterSnapshot> Parameters { get; }

    public CommandParameterSnapshot GetParameter(string name) =>
        _parametersByName[NormalizeName(name)];

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name[0] is '@' or ':' or '?' ? name[1..] : name;
    }
}

public sealed record CommandParameterSnapshot(
    string Name,
    object? Value,
    ParameterDirection Direction,
    DbType? DbType,
    int? Size,
    byte? Precision,
    byte? Scale);

internal sealed class CommandObservation
{
    private readonly ICommandObserver _observer;
    private readonly CommandDefinition _definition;
    private readonly CommandExecutionKind _kind;
    private readonly string _providerName;
    private readonly bool _hasTransaction;
    private readonly long _started;

    private CommandObservation(
        ICommandObserver observer,
        CommandDefinition definition,
        CommandExecutionKind kind,
        string providerName,
        bool hasTransaction)
    {
        _observer = observer;
        _definition = definition;
        _kind = kind;
        _providerName = providerName;
        _hasTransaction = hasTransaction;
        observer.Executing(new CommandExecutingContext(
            new CommandSnapshot(definition),
            kind,
            providerName,
            hasTransaction));
        _started = Stopwatch.GetTimestamp();
    }

    internal static CommandObservation? Start(
        ICommandObserver? observer,
        CommandDefinition definition,
        CommandExecutionKind kind,
        string providerName,
        bool hasTransaction) =>
        observer is null
            ? null
            : new CommandObservation(observer, definition, kind, providerName, hasTransaction);

    internal void Complete(int? affectedRows = null, int? resultCount = null) =>
        _observer.Executed(new CommandExecutedContext(
            new CommandSnapshot(_definition),
            _kind,
            _providerName,
            _hasTransaction,
            Stopwatch.GetElapsedTime(_started),
            affectedRows,
            resultCount));

    internal void Fail(Exception exception)
    {
        try
        {
            _observer.Failed(new CommandFailedContext(
                new CommandSnapshot(_definition),
                _kind,
                _providerName,
                _hasTransaction,
                Stopwatch.GetElapsedTime(_started),
                exception));
        }
        catch
        {
            // Preserve the original command failure.
        }
    }
}
