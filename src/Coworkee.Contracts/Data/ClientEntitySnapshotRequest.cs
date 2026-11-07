namespace Coworkee.Contracts.Data;

/// <summary>
/// The copy of a set as a SQLite file the server builds (<c>POST /api/v1/client-entities/{set}/snapshot</c>): the browser opens it instead of
/// downloading pages of JSON and writing every row. The columns are the client's table: names, how each value is stored, which are indexed.
/// </summary>
/// <param name="Expand">The navigations each row carries, as in $expand; they are JSON columns.</param>
public sealed record ClientEntitySnapshotRequest(IReadOnlyList<ClientEntitySnapshotColumn> Columns, string? Expand);

/// <param name="Kind">One of <see cref="ClientEntitySnapshotKinds"/>.</param>
public sealed record ClientEntitySnapshotColumn(string Name, string Kind, bool Indexed);

/// <summary>How a value is stored, as EF Core's SQLite provider stores it for the client's property.</summary>
public static class ClientEntitySnapshotKinds
{
    /// <summary>TEXT; enums by name.</summary>
    public const string Text = "text";

    /// <summary>TEXT, upper case.</summary>
    public const string Guid = "guid";

    /// <summary>INTEGER.</summary>
    public const string Integer = "int";

    /// <summary>REAL; decimals too.</summary>
    public const string Real = "real";

    /// <summary>INTEGER 0 or 1.</summary>
    public const string Boolean = "bool";

    /// <summary>INTEGER, <see cref="ToInstant"/>.</summary>
    public const string DateTimeOffset = "dto";

    /// <summary>TEXT, "yyyy-MM-dd HH:mm:ss.FFFFFFF".</summary>
    public const string DateTime = "datetime";

    /// <summary>TEXT, "yyyy-MM-dd" (DateOnly).</summary>
    public const string Date = "date";

    /// <summary>INTEGER, the ticks of a TimeOnly or TimeSpan.</summary>
    public const string Ticks = "ticks";

    /// <summary>TEXT, the value as JSON (collections, nested objects, expanded navigations).</summary>
    public const string Json = "json";

    /// <summary>The header with the version the snapshot was taken at (round-trip format).</summary>
    public const string VersionHeader = "X-Coworkee-Version";

    /// <summary>
    /// A DateTimeOffset as one INTEGER that sorts and compares by the instant: the UTC time in units of 0.1 ms, then the offset in 11 bits.
    /// (EF Core's DateTimeOffsetToBinaryConverter puts the local time first, so values of different offsets compare wrong.)
    /// </summary>
    /// <remarks>ponytail: the same instant with two offsets is two values, so "eq" and the bound of a range miss such a row; offsets differ rarely within one column.</remarks>
    public static long ToInstant(System.DateTimeOffset value) => ((value.UtcTicks / 1000) << 11) | (long)(value.Offset.TotalMinutes + 1024);

    /// <summary>The value of <see cref="ToInstant"/>.</summary>
    public static System.DateTimeOffset FromInstant(long value)
    {
        var offset = System.TimeSpan.FromMinutes((value & 0x7FF) - 1024);
        return new System.DateTimeOffset((value >> 11) * 1000 + offset.Ticks, offset);
    }
}
