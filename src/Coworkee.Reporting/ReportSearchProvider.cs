using System.Runtime.CompilerServices;
using Coworkee.Application.GlobalSearch;
using Coworkee.Contracts.Reporting;
using Coworkee.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Reporting;

/// <summary>The reports the user sees (own, organisation, own roles) by name or description.</summary>
internal sealed class ReportSearchProvider : IGlobalSearchProvider
{
    public string Name => "reports";

    public string Group => "Reports";

    public string? Icon => "Assessment";

    public string? Permission => ReportPermissions.View;

    public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var visible = await context.Services.GetRequiredService<ReportAccess>().VisibleAsync(cancellationToken);
        var reports = await query.Words
            .Aggregate(visible.AsNoTracking(), (rows, word) => rows.Where(r => r.Name.ToLower().Contains(word.ToLower()) || (r.Description != null && r.Description.ToLower().Contains(word.ToLower()))))
            .OrderBy(r => r.Name).Take(context.Limit * 2).Select(r => new { r.Id, r.Name, r.Description }).ToListAsync(cancellationToken);
        var hits = reports
            .Select(r => (Report: r, Match: SearchMatch.Best(query, r.Name, r.Description)))
            .Where(r => r.Match is not null)
            .OrderByDescending(r => r.Match!.Score)
            .Take(context.Limit);
        foreach (var (report, match) in hits)
        {
            yield return new GlobalSearchHit(Name, Group, report.Name)
            {
                Subtitle = report.Description,
                Url = $"/reports/{report.Id}",
                Id = report.Id.ToString(),
                Score = match!.Score,
                Highlights = match.Highlights,
            };
        }
    }
}
