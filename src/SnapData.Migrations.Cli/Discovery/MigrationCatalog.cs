using SnapData.Migrations;

namespace SnapData.Migrations.Cli.Discovery;

internal sealed class MigrationCatalog : IReadOnlyList<MigrationDescriptor>
{
    private readonly IReadOnlyList<MigrationDescriptor> descriptors;

    public MigrationCatalog(
        IReadOnlyList<Migration> migrations,
        string? bundleType,
        Func<Migration, string>? detail = null)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        Migrations = migrations;
        BundleType = bundleType;
        descriptors = migrations.Select(migration => new MigrationDescriptor(
            migration.Id,
            detail?.Invoke(migration)
                ?? migration.GetType().FullName
                ?? migration.GetType().Name)).ToArray();
    }

    public MigrationCatalog(
        IReadOnlyList<MigrationDescriptor> descriptors,
        string? bundleType)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        this.descriptors = descriptors;
        Migrations = Array.Empty<Migration>();
        BundleType = bundleType;
    }

    public int Count => descriptors.Count;

    public MigrationDescriptor this[int index] => descriptors[index];

    public string? BundleType { get; }

    public IReadOnlyList<Migration> Migrations { get; }

    public IEnumerator<MigrationDescriptor> GetEnumerator() => descriptors.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
