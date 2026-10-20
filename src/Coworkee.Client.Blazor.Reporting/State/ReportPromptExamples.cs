using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>Three prompts for the AI that name real data sets and fields of the user, so the first try shows what works.</summary>
public static class ReportPromptExamples
{
    public static IReadOnlyList<string> For(IReadOnlyList<ReportSourceDto> sources, CoworkeeLocalizer l)
    {
        var examples = new List<string>();
        foreach (var source in sources.OrderBy(s => s.Computed).Take(3))
        {
            var label = l[source.Label];
            var category = source.Fields.FirstOrDefault(f => f.Kind is ReportFieldKind.Enum or ReportFieldKind.Boolean) ?? source.Fields.FirstOrDefault(f => f.Kind == ReportFieldKind.Text);
            var number = source.Fields.FirstOrDefault(f => f.Kind is ReportFieldKind.Number or ReportFieldKind.Integer);
            var date = source.Fields.FirstOrDefault(f => f.Kind == ReportFieldKind.Date);
            examples.Add((examples.Count, category, number, date) switch
            {
                (0, { } c, _, _) => l["{0} per {1} as a bar chart, with the total as KPI", label, c.Label],
                (1, _, { } n, { } d) => l["Monthly trend of the sum of {0} of {1} by {2}, with a target line", n.Label, label, d.Label],
                (_, { } c, { } n, _) => l["Top 10 {0} of {1} by {2} as a table with colours for good and bad values", c.Label, label, n.Label],
                _ => l["An overview of {0} with KPIs and charts", label],
            });
        }

        return examples;
    }
}
