namespace Coworkee.Contracts.Reporting;

public sealed record ReportDto(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    ReportVisibility Visibility,
    IReadOnlyList<Guid> RoleIds,
    string? OwnerName,
    bool CanEdit,
    bool IsFavorite,
    ReportDefinition Definition,
    DateTimeOffset? ModifiedAt,
    IReadOnlyList<Guid>? GroupIds = null);
