namespace Coworkee.Reporting.Querying;

// a record: in-memory sources group with Enumerable.GroupBy, which needs value equality
internal sealed record GroupKey<TA, TB> : IGroupKey
{
    public TA A { get; init; } = default!;

    public TB B { get; init; } = default!;

    object? IGroupKey.A => A;

    object? IGroupKey.B => B;
}
