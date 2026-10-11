using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

public static class ReportPermissions
{
    public const string GroupName = "Reports";
    public const string View = "Reports.View";
    public const string Manage = "Reports.Manage";
    public const string Share = "Reports.Share";
}

/// <summary>Who sees a report besides its owner.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportVisibility>))]
public enum ReportVisibility
{
    Private,
    Roles,
    Organisation,
}

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
    DateTimeOffset? ModifiedAt);

public sealed record SaveReportRequest(
    string Name,
    string? Description,
    string? Icon,
    ReportVisibility Visibility,
    IReadOnlyList<Guid> RoleIds,
    ReportDefinition Definition);

public sealed record FavoriteReportRequest(bool Favorite);

public sealed record ReportRoleDto(Guid Id, string Name);

/// <summary>The file a report is exported to and imported from.</summary>
public sealed record ReportFile(string Name, string? Description, string? Icon, ReportDefinition Definition)
{
    public const string Extension = ".report.json";
}

/// <summary>A table as a widget shows it, written to an Excel workbook; cells are text, numbers or null.</summary>
public sealed record ReportSheetRequest(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<System.Text.Json.JsonElement>> Rows);
