using Coworkee.Contracts.Reporting;
using FluentValidation;

namespace Coworkee.Reporting;

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
