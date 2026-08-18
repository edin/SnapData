namespace SnapData.Migrations;

public enum MigrationStatusState
{
    Pending,
    Applied,
    Changed,
    Unverifiable,
    Missing,
    OutOfOrder
}

public sealed record MigrationStatusEntry(
    string MigrationId,
    MigrationStatusState State,
    int? BundleOrder = null,
    long? AppliedOrder = null,
    string? StoredFingerprint = null,
    string? CurrentFingerprint = null);

public sealed class MigrationHistoryValidationException : InvalidOperationException
{
    public MigrationHistoryValidationException(
        IEnumerable<MigrationStatusEntry> invalidEntries)
        : base("Migration history is inconsistent with the configured migration bundle.")
    {
        ArgumentNullException.ThrowIfNull(invalidEntries);
        InvalidEntries = Array.AsReadOnly(invalidEntries.ToArray());
    }

    public IReadOnlyList<MigrationStatusEntry> InvalidEntries { get; }
}
