namespace Coworkee.Contracts.DataFilters;

/// <param name="LayerKey">The group or role id; null for System and Tenant.</param>
/// <param name="Query">A <c>QueryGroup</c> as JSON.</param>
/// <param name="Valid">False when the query no longer parses against the model (such a filter lets no rows through).</param>
public sealed record DataFilterDto(Guid Id, string EntitySet, DataFilterLayer Layer, Guid? LayerKey, string Query, bool AppliesToDetails, bool Valid, string? Problem);

public sealed record SaveDataFilterRequest(string EntitySet, DataFilterLayer Layer, Guid? LayerKey, string Query, bool AppliesToDetails = true);

/// <summary>Rows of <paramref name="EntitySet"/> are visible only when the row of <paramref name="TargetSet"/> whose key is <paramref name="Property"/> passes the target's filters.</summary>
public sealed record DataFilterLinkDto(Guid Id, string EntitySet, string Property, string TargetSet);

public sealed record SaveDataFilterLinkRequest(string EntitySet, string Property, string TargetSet);

/// <summary>A property the filter editor offers for a set (one level of single-valued navigations as <c>Nav.Prop</c>); <see cref="Protected"/> properties are seen only by users with their permission, so a filter on them hides rows by something not every user may read.</summary>
public sealed record DataFilterFieldDto(string Name, Contracts.Query.QueryFieldType Type, bool Nullable, bool Flags, IReadOnlyList<Contracts.Query.QueryOption>? Options, bool Protected);
