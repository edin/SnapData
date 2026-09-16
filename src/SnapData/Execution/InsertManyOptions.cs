namespace SnapData;

/// <summary>Bounded insert execution. Limits are budgets, not server discovery.</summary>
public sealed record InsertManyOptions
{
    public int BatchSize { get; init; } = 100;
    public int MaxParametersPerCommand { get; init; } = 500;
    /// <summary>Own a transaction on a session. An existing transaction is always caller-owned.</summary>
    public bool Atomic { get; init; } = true;
}

/// <summary>Counts for a successful call. AffectedRows is null if any provider count was unknown.</summary>
public sealed record InsertManyResult(long RowsSubmitted, long StatementsExecuted, long? AffectedRows);
