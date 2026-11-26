namespace Coworkee.OData.DataFilters;

/// <summary>Ambient switch for the data filters; lasts until disposed, nested scopes restore the outer value.</summary>
public static class DataFilterScope
{
    private static readonly AsyncLocal<bool> ByKeyValue = new();

    public static bool IsByKey => ByKeyValue.Value;

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

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => ByKeyValue.Value = previous;
    }
}
