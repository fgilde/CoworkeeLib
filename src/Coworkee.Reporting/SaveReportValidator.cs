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
        RuleFor(r => r.Definition.DateFields).Must(f => f.Count <= ReportLimits.DateFields).WithMessage($"At most {ReportLimits.DateFields} date fields.");
        RuleFor(r => r.Definition.FacetFields).Must(f => f.Count <= ReportLimits.FacetFields).WithMessage($"At most {ReportLimits.FacetFields} filter bar fields.");
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
                metric.RuleForEach(m => m.Aggregates).ChildRules(Value);
            });
            widget.RuleFor(w => w.Options.ValueIcons).Must(v => v.Count <= ReportLimits.ValueIcons).WithMessage($"At most {ReportLimits.ValueIcons} chips.");
            widget.RuleForEach(w => w.Options.ValueIcons).ChildRules(icon =>
            {
                icon.RuleFor(i => i.Value).MaximumLength(ReportLimits.NameLength);
                icon.RuleFor(i => i.Icon).MaximumLength(ReportLimits.IconLength);
            });
            widget.When(w => w.Query is not null, () =>
            {
                widget.RuleForEach(w => w.Query!.Aggregates).ChildRules(Value);
                widget.RuleForEach(w => w.Query!.Joins).ChildRules(join => join.RuleForEach(j => j.Aggregates).ChildRules(Value));
            });
            widget.RuleForEach(w => w.Options.Buckets).ChildRules(bucket => bucket.RuleFor(b => b.Label).NotEmpty().MaximumLength(ReportViews.LabelLength));
            widget.RuleFor(w => w.Options.Thresholds).Must(t => t.Count <= ReportThresholds.Max).WithMessage($"At most {ReportThresholds.Max} thresholds.");
            widget.RuleForEach(w => w.Options.Thresholds).ChildRules(stop =>
            {
                stop.RuleFor(t => t.Value).Must(double.IsFinite).WithMessage("A threshold is a number.");
                stop.RuleFor(t => t.Color).Must(ReportThresholds.IsValidColor).WithMessage("Use success, warning, error, info or a colour like #2e7d32.");
                stop.RuleFor(t => t.Label).MaximumLength(ReportViews.LabelLength);
            });
            widget.RuleFor(w => w.Query).NotNull().When(w => w.Type is WidgetType.Kpi or WidgetType.Chart or WidgetType.Table or WidgetType.DataTable or WidgetType.Pivot or WidgetType.GroupedTable)
                .WithMessage("Choose a data source.");
            widget.When(w => w.Type == WidgetType.GroupedTable && w.Query is not null, () =>
            {
                widget.RuleFor(w => w.Query!.GroupFields).Must(f => f.Count is >= 1 and <= Querying.ReportTables.MaxGroupFields)
                    .WithMessage($"Group by 1 to {Querying.ReportTables.MaxGroupFields} fields.");
                widget.RuleFor(w => w.Query!.Joins).Must(j => j.Count <= Querying.ReportTables.MaxJoins).WithMessage($"At most {Querying.ReportTables.MaxJoins} joined data sets.");
                widget.RuleFor(w => w.Options.HiddenColumns).Must(h => h.Count <= ReportLimits.HiddenColumns).WithMessage($"At most {ReportLimits.HiddenColumns} hidden columns.");
                widget.RuleFor(w => w.Options.ColumnOrder).Must(o => o is null || o.Count <= ReportLimits.HiddenColumns).WithMessage($"At most {ReportLimits.HiddenColumns} ordered columns.");
                widget.RuleForEach(w => w.Options.ColumnOrder).MaximumLength(ReportViews.LabelLength);
            });
        });
    }

    // a value's texts show in headers, legends and mails
    private static void Value(InlineValidator<ReportAggregate> value)
    {
        value.RuleFor(a => a.Label).MaximumLength(ReportViews.LabelLength);
        value.RuleFor(a => a.Unit).MaximumLength(ReportLimits.UnitLength);
    }

    // images and embedded pages must not run script through javascript: or data: addresses
    internal static bool SafeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) || Contracts.WebAddress.IsWebOrLocalPath(url);
}
