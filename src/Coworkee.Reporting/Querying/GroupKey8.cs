namespace Coworkee.Reporting.Querying;

/// <summary>The key of a grouped table, its members in the order of the group fields.</summary>
internal abstract record GroupKey8
{
    public abstract object?[] Members { get; }
}

/// <summary>Up to 8 group fields; unused members are the constant 0. A record: in-memory sources group with Enumerable.GroupBy, which needs value equality.</summary>
internal sealed record GroupKey8<TA, TB, TC, TD, TE, TF, TG, TH> : GroupKey8
{
    public TA A { get; init; } = default!;

    public TB B { get; init; } = default!;

    public TC C { get; init; } = default!;

    public TD D { get; init; } = default!;

    public TE E { get; init; } = default!;

    public TF F { get; init; } = default!;

    public TG G { get; init; } = default!;

    public TH H { get; init; } = default!;

    public override object?[] Members => [A, B, C, D, E, F, G, H];
}
