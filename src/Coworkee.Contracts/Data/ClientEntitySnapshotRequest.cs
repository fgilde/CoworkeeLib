namespace Coworkee.Contracts.Data;

/// <summary>
/// PoC: the copy of a set as a SQLite file the server builds (<c>POST /api/v1/client-entities/{set}/snapshot</c>): the browser opens it instead of
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

    /// <summary>INTEGER, EF Core's DateTimeOffsetToBinaryConverter.</summary>
    public const string DateTimeOffset = "dto";

    /// <summary>TEXT, "yyyy-MM-dd HH:mm:ss.FFFFFFF".</summary>
    public const string DateTime = "datetime";

    /// <summary>TEXT, the value as JSON (collections, nested objects, expanded navigations).</summary>
    public const string Json = "json";

    /// <summary>The header with the version the snapshot was taken at (round-trip format).</summary>
    public const string VersionHeader = "X-Coworkee-Version";
}
