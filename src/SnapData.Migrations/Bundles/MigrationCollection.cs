using System.Reflection;

namespace SnapData.Migrations;

public sealed class MigrationCollection : IReadOnlyList<Migration>
{
    private readonly List<Migration> migrations = [];
    private readonly HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
    private bool isSealed;

    public int Count => migrations.Count;

    public Migration this[int index] => migrations[index];

    public MigrationCollection Add<TMigration>()
        where TMigration : Migration, new() =>
        Add(new TMigration());

    public MigrationCollection Add(Migration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(migration.Id))
        {
            throw new InvalidOperationException(
                $"Migration type '{migration.GetType().FullName}' returned an empty ID.");
        }

        if (!ids.Add(migration.Id))
        {
            throw new InvalidOperationException(
                $"Migration ID '{migration.Id}' is already registered.");
        }

        migrations.Add(migration);
        return this;
    }

    public MigrationCollection AddRange(IEnumerable<Migration> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var migration in items)
        {
            Add(migration);
        }

        return this;
    }

    public MigrationCollection ScanAssembly(
        Assembly assembly,
        Func<Type, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return ScanTypes(assembly.GetTypes(), predicate);
    }

    public MigrationCollection ScanTypes(
        IEnumerable<Type> types,
        Func<Type, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        EnsureOpen();
        var discovered = types
            .Where(type => predicate?.Invoke(type) ?? true)
            .Where(type =>
                typeof(Migration).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.ContainsGenericParameters)
            .Select(CreateMigration)
            .OrderBy(migration => migration.Id, StringComparer.Ordinal)
            .ToArray();

        return AddRange(discovered);
    }

    public IEnumerator<Migration> GetEnumerator() => migrations.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    internal IReadOnlyList<Migration> Seal()
    {
        isSealed = true;
        return Array.AsReadOnly(migrations.ToArray());
    }

    private void EnsureOpen()
    {
        if (isSealed)
        {
            throw new InvalidOperationException("The migration collection is sealed.");
        }
    }

    private static Migration CreateMigration(Type type)
    {
        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Migration type '{type.FullName}' needs a public parameterless constructor.");
        }

        return (Migration)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(
                $"Migration type '{type.FullName}' could not be created."));
    }
}
