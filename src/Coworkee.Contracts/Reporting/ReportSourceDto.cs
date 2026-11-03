namespace Coworkee.Contracts.Reporting;

/// <summary>An entity set or app source reports can use, with the fields the signed-in user may group, aggregate and filter by.</summary>
/// <param name="Topic">Realtime topic announcing changes of its rows, if the entity is [Realtime].</param>
/// <param name="Computed">A source the app computes (no OData set): its rows come from <c>POST /api/v1/reports/rows</c>, it has no facets.</param>
public sealed record ReportSourceDto(string EntitySet, string Label, IReadOnlyList<ReportFieldDto> Fields, string? Topic = null, bool Computed = false);
