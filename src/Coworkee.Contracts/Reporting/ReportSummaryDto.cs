namespace Coworkee.Contracts.Reporting;

public sealed record ReportSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string? Icon,
    ReportVisibility Visibility,
    string? OwnerName,
    bool IsOwner,
    bool IsFavorite,
    int Widgets,
    IReadOnlyList<string> EntitySets,
    DateTimeOffset? ModifiedAt);
