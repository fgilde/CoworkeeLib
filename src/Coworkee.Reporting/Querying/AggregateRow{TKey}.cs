namespace Coworkee.Reporting.Querying;

internal sealed class AggregateRow<TKey> : IAggregateRow
{
    public TKey Key { get; init; } = default!;

    public double? V0 { get; init; }

    public double? V1 { get; init; }

    public double? V2 { get; init; }

    public double? V3 { get; init; }

    public double? V4 { get; init; }

    public double? V5 { get; init; }

    public double? V6 { get; init; }

    public double? V7 { get; init; }

    object? IAggregateRow.Key => Key;

    public double?[] Values => [V0, V1, V2, V3, V4, V5, V6, V7];
}
