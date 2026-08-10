using System.Collections;
using System.Data;
using System.Data.Common;

namespace SnapData;

public sealed class ParameterSet : IReadOnlyDictionary<string, object?>
{
    private readonly Dictionary<string, CommandParameter> _parameters =
        new(StringComparer.OrdinalIgnoreCase);

    public static ParameterSet Empty => new();

    public int Count => _parameters.Count;

    public IEnumerable<string> Keys => _parameters.Keys;

    public IEnumerable<object?> Values => _parameters.Values.Select(parameter => parameter.Value);

    public object? this[string key] => _parameters[NormalizeName(key)].Value;

    public ParameterSet Input(
        string name,
        object? value,
        DbType? dbType = null,
        int? size = null) =>
        Add(new CommandParameter(name, value, ParameterDirection.Input, dbType, size));

    public ParameterSet Output<T>(
        string name,
        DbType? dbType = null,
        int? size = null) =>
        Add(new CommandParameter(
            name,
            null,
            ParameterDirection.Output,
            dbType ?? InferDbType(typeof(T)),
            size));

    public ParameterSet InputOutput(
        string name,
        object? value,
        DbType? dbType = null,
        int? size = null) =>
        Add(new CommandParameter(name, value, ParameterDirection.InputOutput, dbType, size));

    public ParameterSet ReturnValue<T>(string name = "return_value", DbType? dbType = null) =>
        Add(new CommandParameter(
            name,
            null,
            ParameterDirection.ReturnValue,
            dbType ?? InferDbType(typeof(T))));

    public ParameterSet Add(CommandParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _parameters.Add(NormalizeName(parameter.Name), parameter);
        return this;
    }

    public T? Get<T>(string name)
    {
        var value = this[name];
        return value is null or DBNull
            ? default
            : (T?)RowMapper<T>.ConvertValue(value, typeof(T));
    }

    public CommandParameter GetParameter(string name) =>
        _parameters[NormalizeName(name)];

    public bool ContainsKey(string key) => _parameters.ContainsKey(NormalizeName(key));

    public bool TryGetValue(string key, out object? value)
    {
        if (_parameters.TryGetValue(NormalizeName(key), out var parameter))
        {
            value = parameter.Value;
            return true;
        }

        value = null;
        return false;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
        _parameters.Select(pair =>
            new KeyValuePair<string, object?>(pair.Key, pair.Value.Value)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal IEnumerable<CommandParameter> Definitions => _parameters.Values;

    internal void CaptureOutput(DbParameterCollection parameters)
    {
        foreach (CommandParameter definition in _parameters.Values)
        {
            if (definition.Direction == ParameterDirection.Input)
            {
                continue;
            }

            var parameter = parameters.Cast<DbParameter>().FirstOrDefault(candidate =>
                NormalizeName(candidate.ParameterName).Equals(
                    NormalizeName(definition.Name),
                    StringComparison.OrdinalIgnoreCase));
            if (parameter is not null)
            {
                definition.SetValue(parameter.Value is DBNull ? null : parameter.Value);
            }
        }
    }

    internal static ParameterSet From(object? values)
    {
        if (values is ParameterSet set)
        {
            return set;
        }

        var result = new ParameterSet();
        foreach (var pair in ParameterReader.Read(values))
        {
            result.Input(pair.Key, pair.Value);
        }

        return result;
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name[0] is '@' or ':' or '?' ? name[1..] : name;
    }

    private static DbType? InferDbType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => System.Data.DbType.Boolean,
            TypeCode.Byte => System.Data.DbType.Byte,
            TypeCode.SByte => System.Data.DbType.SByte,
            TypeCode.Int16 => System.Data.DbType.Int16,
            TypeCode.UInt16 => System.Data.DbType.UInt16,
            TypeCode.Int32 => System.Data.DbType.Int32,
            TypeCode.UInt32 => System.Data.DbType.UInt32,
            TypeCode.Int64 => System.Data.DbType.Int64,
            TypeCode.UInt64 => System.Data.DbType.UInt64,
            TypeCode.Single => System.Data.DbType.Single,
            TypeCode.Double => System.Data.DbType.Double,
            TypeCode.Decimal => System.Data.DbType.Decimal,
            TypeCode.DateTime => System.Data.DbType.DateTime,
            TypeCode.Char => System.Data.DbType.StringFixedLength,
            TypeCode.String => System.Data.DbType.String,
            _ when type == typeof(Guid) => System.Data.DbType.Guid,
            _ when type == typeof(DateTimeOffset) => System.Data.DbType.DateTimeOffset,
            _ when type == typeof(TimeSpan) => System.Data.DbType.Time,
            _ when type == typeof(byte[]) => System.Data.DbType.Binary,
            _ => null
        };
    }
}

public sealed class CommandParameter
{
    public CommandParameter(
        string name,
        object? value = null,
        ParameterDirection direction = ParameterDirection.Input,
        DbType? dbType = null,
        int? size = null,
        byte? precision = null,
        byte? scale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Value = value;
        Direction = direction;
        DbType = dbType;
        Size = size;
        Precision = precision;
        Scale = scale;
    }

    public string Name { get; }

    public object? Value { get; private set; }

    public ParameterDirection Direction { get; }

    public DbType? DbType { get; }

    public int? Size { get; }

    public byte? Precision { get; }

    public byte? Scale { get; }

    internal void SetValue(object? value) => Value = value;
}
