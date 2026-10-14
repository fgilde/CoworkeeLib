namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Ranks of a column a filter accepts: a range from..to (comparisons and their "and", most facet options) or any set. Ranges stay ranges,
/// so a date range facet over 100,000 distinct dates adds up two prefix sums instead of walking every value.
/// </summary>
internal readonly record struct RankSet(int From, int To, bool[]? Set)
{
    public static RankSet Range(int from, int to) => new(from, Math.Max(from, to), null);

    public static RankSet Of(bool[] set) => new(0, 0, set);

    public RankSet And(RankSet other, int count) => Set is null && other.Set is null
        ? Range(Math.Max(From, other.From), Math.Min(To, other.To))
        : Of(Combine(other, count, (a, b) => a && b));

    public RankSet Or(RankSet other, int count) => Of(Combine(other, count, (a, b) => a || b));

    public RankSet Not(int count)
    {
        var set = Mask(count);
        var inverted = new bool[count];
        for (var rank = 0; rank < count; rank++)
        {
            inverted[rank] = !set[rank];
        }

        return Of(inverted);
    }

    public bool[] Mask(int count)
    {
        if (Set is { } set)
        {
            return set;
        }

        var mask = new bool[count];
        Array.Fill(mask, true, From, To - From);
        return mask;
    }

    /// <summary>The rows of the accepted ranks; <paramref name="prefix"/> holds the row count below each rank.</summary>
    public int Sum(int[] prefix)
    {
        if (Set is null)
        {
            return prefix[To] - prefix[From];
        }

        var sum = 0;
        for (var rank = 0; rank < Set.Length; rank++)
        {
            if (Set[rank])
            {
                sum += prefix[rank + 1] - prefix[rank];
            }
        }

        return sum;
    }

    private bool[] Combine(RankSet other, int count, Func<bool, bool, bool> both)
    {
        var (left, right) = (Mask(count), other.Mask(count));
        var result = new bool[count];
        for (var rank = 0; rank < count; rank++)
        {
            result[rank] = both(left[rank], right[rank]);
        }

        return result;
    }
}
