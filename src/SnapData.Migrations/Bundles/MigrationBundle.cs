namespace SnapData.Migrations;

public abstract class MigrationBundle : IReadOnlyList<Migration>
{
    private readonly Lazy<IReadOnlyList<Migration>> migrations;

    protected MigrationBundle()
    {
        migrations = new Lazy<IReadOnlyList<Migration>>(
            Build,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int Count => migrations.Value.Count;

    public Migration this[int index] => migrations.Value[index];

    protected abstract void Configure(MigrationCollection migrations);

    public IEnumerator<Migration> GetEnumerator() => migrations.Value.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    private IReadOnlyList<Migration> Build()
    {
        var collection = new MigrationCollection();
        Configure(collection);
        return collection.Seal();
    }
}
