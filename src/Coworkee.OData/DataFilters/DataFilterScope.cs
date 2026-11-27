namespace Coworkee.OData.DataFilters;

/// <summary>Ambient switch for the data filters; lasts until disposed, nested scopes restore the outer value.</summary>
public static class DataFilterScope
{
    private static readonly AsyncLocal<bool> ByKeyValue = new();

    private static readonly AsyncLocal<bool> OffValue = new();

    public static bool IsByKey => ByKeyValue.Value;

    /// <summary>True while <see cref="Off"/> is active: no data filter applies.</summary>
    public static bool IsOff => OffValue.Value;

    /// <summary>No data filter applies (the app's own row filters still do), e.g. to count what a draft filter would start from.</summary>
    public static IDisposable Off()
    {
        var previous = OffValue.Value;
        OffValue.Value = true;
        return new RestoreOff(previous);
    }

    /// <summary>
    /// A read of single records (OData by key, opening a record by id): filters that do not apply to details are skipped. Wrap only the read of the set's own
    /// rows: what such a read expands is a list.
    /// </summary>
    public static IDisposable ByKey()
    {
        var previous = ByKeyValue.Value;
        ByKeyValue.Value = true;
        return new Restore(previous);
    }

    private sealed class RestoreOff(bool previous) : IDisposable
    {
        public void Dispose() => OffValue.Value = previous;
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => ByKeyValue.Value = previous;
    }
}
