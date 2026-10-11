using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>The fields of a report source for the query builder.</summary>
public static class ReportQueryFields
{
    public static IReadOnlyList<QueryField> From(ReportSourceDto? source) =>
        [.. (source?.Fields ?? []).Select(f => new QueryField(f.Path, f.Label, f.Kind switch
        {
            ReportFieldKind.Integer or ReportFieldKind.Number => QueryFieldType.Number,
            ReportFieldKind.Date => f.DateOnly ? QueryFieldType.Date : QueryFieldType.DateTime,
            ReportFieldKind.Boolean => QueryFieldType.Boolean,
            ReportFieldKind.Enum => QueryFieldType.Enum,
            ReportFieldKind.Guid => QueryFieldType.Guid,
            _ => QueryFieldType.Text,
        })
        {
            Options = f.Members is { Count: > 0 } members ? [.. members.Select(m => new QueryOption(m, f.MemberLabel(m)))] : null,
            Category = f.Path.Contains('/') ? f.Path[..f.Path.IndexOf('/')] : null,
            Nullable = true,
        })];
}
