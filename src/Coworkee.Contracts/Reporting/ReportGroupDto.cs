namespace Coworkee.Contracts.Reporting;

/// <summary>A group a report can be shared with; <see cref="Code"/> orders the tree (a parent's code starts its children's).</summary>
public sealed record ReportGroupDto(Guid Id, Guid? ParentId, string Code, string Name);
