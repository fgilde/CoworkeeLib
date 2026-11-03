namespace Coworkee.Contracts.Reporting;

/// <param name="Path">OData path, e.g. "Status" or "Customer/Name".</param>
/// <param name="EnumType">Qualified OData name of enum fields.</param>
/// <param name="DateOnly">A date without time, filtered with literals like 2026-03-01.</param>
/// <param name="MemberLabels">Display names of <paramref name="Members"/>, in the same order.</param>
public sealed record ReportFieldDto(
    string Path, string Label, ReportFieldKind Kind, IReadOnlyList<string>? Members = null, string? EnumType = null, bool DateOnly = false, IReadOnlyList<string>? MemberLabels = null)
{
    public string MemberLabel(string member) =>
        Members?.ToList().IndexOf(member) is >= 0 and var i && MemberLabels is { } labels && i < labels.Count ? labels[i] : member;
}
