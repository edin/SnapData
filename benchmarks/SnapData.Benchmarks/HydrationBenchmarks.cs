using System.Data;
using System.Data.Common;
using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace SnapData.Benchmarks;

public abstract class HydrationBenchmarkBase
{
    private DataTable _table = null!;

    [GlobalSetup]
    public void Setup()
    {
        _table = new DataTable();
        _table.Columns.Add("id", typeof(long));
        _table.Columns.Add("name", typeof(string));
        _table.Columns.Add("active", typeof(bool));
        for (var id = 1; id <= 1000; id++)
        {
            _table.Rows.Add((long)id, $"User {id}", id % 2 == 0);
        }

        using var reader = CreateReader();
        InitializeMapper(reader);
    }

    protected DbDataReader CreateReader() => _table.CreateDataReader();

    protected abstract void InitializeMapper(DbDataReader reader);
}

[MemoryDiagnoser]
public class MutableHydrationBenchmarks : HydrationBenchmarkBase
{
    private static readonly PropertyInfo IdProperty = typeof(MutableUser)
        .GetProperty(nameof(MutableUser.Id))!;
    private static readonly PropertyInfo NameProperty = typeof(MutableUser)
        .GetProperty(nameof(MutableUser.Name))!;
    private static readonly PropertyInfo ActiveProperty = typeof(MutableUser)
        .GetProperty(nameof(MutableUser.Active))!;
    private static readonly Action<MutableUser, long> SetId = IdProperty.SetMethod!
        .CreateDelegate<Action<MutableUser, long>>();
    private static readonly Action<MutableUser, string> SetName = NameProperty.SetMethod!
        .CreateDelegate<Action<MutableUser, string>>();
    private static readonly Action<MutableUser, bool> SetActive = ActiveProperty.SetMethod!
        .CreateDelegate<Action<MutableUser, bool>>();
    private static readonly IHydrationOperation<MutableUser>[] Operations =
    [
        new Int64HydrationOperation<MutableUser>(0, SetId),
        new StringHydrationOperation<MutableUser>(1, SetName),
        new BooleanHydrationOperation<MutableUser>(2, SetActive)
    ];
    private IRowMapper<MutableUser> _mapper = null!;

    [Params(10, 100, 1000)]
    public int RowCount { get; set; }

    protected override void InitializeMapper(DbDataReader reader)
    {
        _mapper = RowMapper<MutableUser>.Create(
            reader,
            EntityMappingProvider.Default);
    }

    [Benchmark]
    public List<MutableUser> SnapData()
    {
        using var reader = CreateReader();
        var results = new List<MutableUser>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            results.Add(_mapper.Map(reader));
        }

        return results;
    }

    [Benchmark]
    public List<MutableUser> GenericActivatorWithTypedSetters()
    {
        using var reader = CreateReader();
        var results = new List<MutableUser>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            var user = Activator.CreateInstance<MutableUser>();
            user.Id = reader.GetInt64(0);
            user.Name = reader.GetString(1);
            user.Active = reader.GetBoolean(2);
            results.Add(user);
        }

        return results;
    }

    [Benchmark]
    public List<MutableUser> GenericActivatorWithDelegates()
    {
        using var reader = CreateReader();
        var results = new List<MutableUser>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            var user = Activator.CreateInstance<MutableUser>();
            SetId(user, reader.GetInt64(0));
            SetName(user, reader.GetString(1));
            SetActive(user, reader.GetBoolean(2));
            results.Add(user);
        }

        return results;
    }

    [Benchmark]
    public List<MutableUser> GenericActivatorWithOperations()
    {
        using var reader = CreateReader();
        var results = new List<MutableUser>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            var user = Activator.CreateInstance<MutableUser>();
            foreach (var operation in Operations)
            {
                operation.Apply(reader, user);
            }

            results.Add(user);
        }

        return results;
    }

    [Benchmark]
    public List<MutableUser> GenericActivatorWithReflection()
    {
        using var reader = CreateReader();
        var results = new List<MutableUser>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            var user = Activator.CreateInstance<MutableUser>();
            IdProperty.SetValue(user, reader.GetValue(0));
            NameProperty.SetValue(user, reader.GetValue(1));
            ActiveProperty.SetValue(user, reader.GetValue(2));
            results.Add(user);
        }

        return results;
    }

    [Benchmark(Baseline = true)]
    public List<MutableUser> Manual()
    {
        using var reader = CreateReader();
        var results = new List<MutableUser>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            results.Add(new MutableUser
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Active = reader.GetBoolean(2)
            });
        }

        return results;
    }
}

[MemoryDiagnoser]
public class ConstructorHydrationBenchmarks : HydrationBenchmarkBase
{
    private static readonly ConstructorInfo Constructor = typeof(UserRecord)
        .GetConstructors()
        .Single();
    private IRowMapper<UserRecord> _mapper = null!;

    [Params(10, 100, 1000)]
    public int RowCount { get; set; }

    protected override void InitializeMapper(DbDataReader reader)
    {
        _mapper = RowMapper<UserRecord>.Create(
            reader,
            EntityMappingProvider.Default);
    }

    [Benchmark]
    public List<UserRecord> SnapData()
    {
        using var reader = CreateReader();
        var results = new List<UserRecord>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            results.Add(_mapper.Map(reader));
        }

        return results;
    }

    [Benchmark]
    public List<UserRecord> ConstructorInvokeOnly()
    {
        using var reader = CreateReader();
        var results = new List<UserRecord>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            var arguments = new object?[]
            {
                reader.GetValue(0),
                reader.GetValue(1),
                reader.GetValue(2)
            };
            results.Add((UserRecord)Constructor.Invoke(arguments));
        }

        return results;
    }

    [Benchmark(Baseline = true)]
    public List<UserRecord> Manual()
    {
        using var reader = CreateReader();
        var results = new List<UserRecord>(RowCount);
        while (results.Count < RowCount && reader.Read())
        {
            results.Add(new UserRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetBoolean(2)));
        }

        return results;
    }
}

[MemoryDiagnoser]
public class MapperCreationBenchmarks : HydrationBenchmarkBase
{
    protected override void InitializeMapper(DbDataReader reader)
    {
    }

    [Benchmark]
    public object Mutable()
    {
        using var reader = CreateReader();
        return RowMapper<MutableUser>.Create(
            reader,
            EntityMappingProvider.Default);
    }

    [Benchmark]
    public object Constructor()
    {
        using var reader = CreateReader();
        return RowMapper<UserRecord>.Create(
            reader,
            EntityMappingProvider.Default);
    }
}

[Table("users")]
public sealed class MutableUser
{
    [Key]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; }
}

[Table("users")]
public sealed record UserRecord(long Id, string Name, bool Active);

internal interface IHydrationOperation<in TEntity>
{
    void Apply(DbDataReader reader, TEntity entity);
}

internal sealed class Int64HydrationOperation<TEntity>(
    int ordinal,
    Action<TEntity, long> setter) : IHydrationOperation<TEntity>
{
    public void Apply(DbDataReader reader, TEntity entity) =>
        setter(entity, reader.GetInt64(ordinal));
}

internal sealed class StringHydrationOperation<TEntity>(
    int ordinal,
    Action<TEntity, string> setter) : IHydrationOperation<TEntity>
{
    public void Apply(DbDataReader reader, TEntity entity) =>
        setter(entity, reader.GetString(ordinal));
}

internal sealed class BooleanHydrationOperation<TEntity>(
    int ordinal,
    Action<TEntity, bool> setter) : IHydrationOperation<TEntity>
{
    public void Apply(DbDataReader reader, TEntity entity) =>
        setter(entity, reader.GetBoolean(ordinal));
}
