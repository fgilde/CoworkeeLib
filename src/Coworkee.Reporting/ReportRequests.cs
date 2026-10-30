using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Reporting;

[RequiresPermission(ReportPermissions.View)]
[Description("Lists the reports and dashboards the signed-in user can open, favorites first.")]
public sealed record GetReportsQuery : IQuery<Result<IReadOnlyList<ReportSummaryDto>>>;

[RequiresPermission(ReportPermissions.View)]
[Description("Reads a report with its widgets, layout and filters.")]
public sealed record GetReportQuery(Guid Id) : IQuery<Result<ReportDto>>;

[RequiresPermission(ReportPermissions.Manage)]
[Description("Saves a new report of the user with name, description, sharing and definition (e.g. one from generate_report).")]
public sealed record CreateReportCommand(SaveReportRequest Report) : ICommand<Result<ReportDto>>;

[RequiresPermission(ReportPermissions.Manage)]
[Description("Replaces name, description, sharing and definition of a report the user owns (e.g. with one from generate_report).")]
public sealed record UpdateReportCommand(Guid Id, SaveReportRequest Report) : ICommand<Result<ReportDto>>;

[RequiresPermission(ReportPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record DeleteReportCommand(Guid Id) : ICommand<Result>;

[RequiresPermission(ReportPermissions.Manage)]
[Description("Copies a report the user can open into a new private report of the user.")]
public sealed record DuplicateReportCommand(Guid Id) : ICommand<Result<ReportDto>>;

[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
public sealed record SetReportFavoriteCommand(Guid Id, bool Favorite) : ICommand<Result>;

[RequiresPermission(ReportPermissions.View)]
[Description("Reads which report the signed-in user sees as dashboard: the own choice, else the default of the organisation.")]
public sealed record GetDashboardReportQuery : IQuery<Result<DashboardReportDto>>;

[RequiresPermission(ReportPermissions.View)]
[AiTool(Exclude = true)]
public sealed record SetMyDashboardReportCommand(Guid? ReportId) : ICommand<Result>;

[RequiresPermission(ReportPermissions.Dashboard)]
[AiTool(Exclude = true)]
public sealed record SetOrganisationDashboardReportCommand(Guid? ReportId) : ICommand<Result>;

[RequiresPermission(ReportPermissions.Share)]
[AiTool(Exclude = true)]
public sealed record GetReportRolesQuery : IQuery<Result<IReadOnlyList<ReportRoleDto>>>;

[RequiresPermission(ReportPermissions.Share)]
[AiTool(Exclude = true)]
public sealed record GetReportGroupsQuery : IQuery<Result<IReadOnlyList<ReportGroupDto>>>;

[RequiresPermission(ReportPermissions.View)]
[Description("Lists the data sets reports can show, with their fields, kinds and enum values.")]
public sealed record GetReportSourcesQuery : IQuery<Result<IReadOnlyList<ReportSourceDto>>>;

[RequiresPermission(ReportPermissions.View)]
[Description("Aggregates a data set on the server: OData filter, group by up to two fields (dates by day, week, month, quarter or year) and count, sum, average, min, max or distinct count.")]
public sealed record RunReportQuery(ReportQueryRequest Request) : IQuery<Result<ReportQueryResult>>;

[RequiresPermission(ReportPermissions.View)]
[Description("Reads a page of rows of a computed report source (one whose Computed flag is set), with the chosen fields, an OData filter and an order.")]
public sealed record ReadReportRowsQuery(ReportRowsRequest Request) : IQuery<Result<ReportRowsResult>>;

[RequiresPermission(ReportPermissions.Manage)]
[AiTool("Builds a report definition (widgets on a 12 column grid, values, groupings, filters) from a description with the app's AI model, checked against the data sets the user may read; pass the current definition to change it. Returns it unsaved: save it with create_report or update_report.", Name = "generate_report")]
public sealed record GenerateReportDefinitionQuery(string Prompt, ReportDefinition? Current = null) : IQuery<Result<ReportDefinition>>;

[RequiresPermission(ReportPermissions.Manage)]
[AiTool(Exclude = true)]
public sealed record GetReportAiStatusQuery : IQuery<Result<ReportAiStatusDto>>;

internal sealed class GenerateReportDefinitionValidator : AbstractValidator<GenerateReportDefinitionQuery>
{
    public GenerateReportDefinitionValidator() => RuleFor(q => q.Prompt).NotEmpty().MaximumLength(4000);
}

internal sealed class CreateReportValidator : AbstractValidator<CreateReportCommand>
{
    public CreateReportValidator() => RuleFor(c => c.Report).SetValidator(new SaveReportValidator());
}

internal sealed class UpdateReportValidator : AbstractValidator<UpdateReportCommand>
{
    public UpdateReportValidator() => RuleFor(c => c.Report).SetValidator(new SaveReportValidator());
}

internal sealed class SaveReportValidator : AbstractValidator<SaveReportRequest>
{
    public SaveReportValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(ReportLimits.NameLength);
        RuleFor(r => r.Description).MaximumLength(ReportLimits.DescriptionLength);
        RuleFor(r => r.Icon).MaximumLength(ReportLimits.IconLength);
        RuleFor(r => r.RoleIds).NotEmpty().When(r => r.Visibility == ReportVisibility.Roles).WithMessage("Choose the roles to share with.");
        RuleFor(r => r.GroupIds).NotEmpty().When(r => r.Visibility == ReportVisibility.Groups).WithMessage("Choose the groups to share with.");
        RuleFor(r => r.GroupIds).Must(g => g is null || g.Count <= ReportLimits.Groups).WithMessage($"At most {ReportLimits.Groups} groups.");
        RuleFor(r => r.Definition).NotNull();
        RuleFor(r => r.Definition.RefreshSeconds).Must(s => s == 0 || s is >= 10 and <= 86_400).WithMessage("Refresh every 10 seconds to a day, or never.");
        RuleFor(r => r.Definition.Widgets).Must(w => w.Count <= ReportLimits.Widgets).WithMessage($"At most {ReportLimits.Widgets} widgets.");
        RuleFor(r => r.Definition.Widgets).Must(w => w.Select(x => x.Id).Distinct().Count() == w.Count).WithMessage("Widget ids are unique.");
        RuleForEach(r => r.Definition.Widgets).ChildRules(widget =>
        {
            widget.RuleFor(w => w.Id).NotEmpty().MaximumLength(40);
            widget.RuleFor(w => w.Title).MaximumLength(ReportLimits.NameLength);
            widget.RuleFor(w => w.X).InclusiveBetween(0, ReportDefinition.GridColumns - 1);
            widget.RuleFor(w => w.W).InclusiveBetween(1, ReportDefinition.GridColumns);
            widget.RuleFor(w => w.Y).InclusiveBetween(0, 1000);
            widget.RuleFor(w => w.H).InclusiveBetween(1, 40);
            widget.RuleFor(w => w.Options.Text).MaximumLength(ReportLimits.TextLength);
            widget.RuleFor(w => w.AppData).Must(d => d is null || System.Text.Json.JsonSerializer.Serialize(d).Length <= ReportLimits.AppDataLength)
                .WithMessage($"At most {ReportLimits.AppDataLength} characters of app data.");
            widget.RuleFor(w => w.Options.Url).Must(SafeUrl).WithMessage("Use an http or https address.");
            widget.RuleFor(w => w.Options.Metrics).Must(m => m.Count <= ReportViews.Max).WithMessage($"At most {ReportViews.Max} values to switch to.");
            widget.RuleFor(w => w.Options.Buckets).Must(b => b.Count <= ReportViews.Max).WithMessage($"At most {ReportViews.Max} groupings to switch to.");
            widget.RuleForEach(w => w.Options.Metrics).ChildRules(metric =>
            {
                metric.RuleFor(m => m.Label).NotEmpty().MaximumLength(ReportViews.LabelLength);
                metric.RuleFor(m => m.Aggregates).Must(a => a.Count <= ReportLimits.Aggregates).WithMessage($"At most {ReportLimits.Aggregates} values.");
            });
            widget.RuleForEach(w => w.Options.Buckets).ChildRules(bucket => bucket.RuleFor(b => b.Label).NotEmpty().MaximumLength(ReportViews.LabelLength));
            widget.RuleFor(w => w.Options.Thresholds).Must(t => t.Count <= ReportThresholds.Max).WithMessage($"At most {ReportThresholds.Max} thresholds.");
            widget.RuleForEach(w => w.Options.Thresholds).ChildRules(stop =>
            {
                stop.RuleFor(t => t.Value).Must(double.IsFinite).WithMessage("A threshold is a number.");
                stop.RuleFor(t => t.Color).Must(ReportThresholds.IsValidColor).WithMessage("Use success, warning, error, info or a colour like #2e7d32.");
                stop.RuleFor(t => t.Label).MaximumLength(ReportViews.LabelLength);
            });
            widget.RuleFor(w => w.Query).NotNull().When(w => w.Type is WidgetType.Kpi or WidgetType.Chart or WidgetType.Table or WidgetType.DataTable or WidgetType.Pivot)
                .WithMessage("Choose a data source.");
        });
    }

    // images and embedded pages must not run script through javascript: or data: addresses
    internal static bool SafeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) || Contracts.WebAddress.IsWebOrLocalPath(url);
}
