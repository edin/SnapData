namespace SnapData;

/// <summary>Operations advertised by a compiler, not a live server capability probe.</summary>
/// <remarks>Server versions, table features and permissions can impose additional limits.
/// False means not advertised; null limits mean unknown and require caller/provider configuration.</remarks>
public sealed record QueryCapabilities
{
    public static QueryCapabilities None { get; } = new();
    public bool ForUpdate { get; init; }
    public bool Upsert { get; init; }
    public bool UpsertDoNothing { get; init; }
    public bool InsertReturning { get; init; }
    public bool UpdateReturning { get; init; }
    public bool DeleteReturning { get; init; }
    public bool UpsertReturning { get; init; }
    public bool MultiRowInsert { get; init; }
    public int? MaxParametersPerCommand { get; init; }
    public int? MaxRowsPerInsert { get; init; }
}
