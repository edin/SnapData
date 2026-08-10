namespace SnapData;

public sealed record QueryOptions
{
    public int? CommandTimeout { get; init; }
}
