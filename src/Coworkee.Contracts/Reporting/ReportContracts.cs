using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Reporting;

public static class ReportPermissions
{
    public const string GroupName = "Reports";
    public const string View = "Reports.View";
    public const string Manage = "Reports.Manage";
    public const string Share = "Reports.Share";
    public const string Dashboard = "Reports.Dashboard";
    public const string Subscriptions = "Reports.Subscriptions";
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

/// <summary>The report shown as dashboard (the user's choice, else the organisation's), and both choices as stored.</summary>
public sealed record DashboardReportDto(Guid? ReportId, Guid? Mine, Guid? Organisation);

/// <summary>A report as dashboard, or none to go back to the default.</summary>
public sealed record SetDashboardReportRequest(Guid? ReportId);

public sealed record ReportRoleDto(Guid Id, string Name);

/// <summary>The file a report is exported to and imported from.</summary>
public sealed record ReportFile(string Name, string? Description, string? Icon, ReportDefinition Definition)
{
    public const string Extension = ".report.json";
}

/// <summary>A table as a widget shows it, written to an Excel workbook; cells are text, numbers or null.</summary>
public sealed record ReportSheetRequest(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<System.Text.Json.JsonElement>> Rows);

/// <summary>A report to build from <see cref="Prompt"/> with the app's AI model, or <see cref="Current"/> to change; the answer is not saved.</summary>
public sealed record GenerateReportRequest(string Prompt, ReportDefinition? Current = null);

/// <summary>Whether reports can be built with AI: a model is configured and the user may create reports.</summary>
public sealed record ReportAiStatusDto(bool Available);
