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
[AiTool(Exclude = true)]
public sealed record CreateReportCommand(SaveReportRequest Report) : ICommand<Result<ReportDto>>;

[RequiresPermission(ReportPermissions.Manage)]
[AiTool(Exclude = true)]
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

[RequiresPermission(ReportPermissions.Share)]
[AiTool(Exclude = true)]
public sealed record GetReportRolesQuery : IQuery<Result<IReadOnlyList<ReportRoleDto>>>;

[RequiresPermission(ReportPermissions.View)]
[Description("Lists the data sets reports can show, with their fields, kinds and enum values.")]
public sealed record GetReportSourcesQuery : IQuery<Result<IReadOnlyList<ReportSourceDto>>>;

[RequiresPermission(ReportPermissions.View)]
[Description("Aggregates a data set on the server: OData filter, group by up to two fields (dates by day, week, month, quarter or year) and count, sum, average, min, max or distinct count.")]
public sealed record RunReportQuery(ReportQueryRequest Request) : IQuery<Result<ReportQueryResult>>;

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
            widget.RuleFor(w => w.Options.Url).Must(SafeUrl).WithMessage("Use an http or https address.");
            widget.RuleFor(w => w.Query).NotNull().When(w => w.Type is WidgetType.Kpi or WidgetType.Chart or WidgetType.Table or WidgetType.DataTable or WidgetType.Pivot)
                .WithMessage("Choose a data source.");
        });
    }

    // images and embedded pages must not run script through javascript: or data: addresses
    internal static bool SafeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || (Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute.Scheme is "https" or "http" : url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal));
}
