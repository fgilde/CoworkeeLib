using Coworkee.Application.Messaging;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Reporting;

/// <summary>The user's own dashboard report wins over the organisation's; a report the user cannot see (any more) is skipped.</summary>
internal sealed class DashboardHandlers(CoworkeeDbContext db, ReportAccess access, ICurrentUser currentUser)
    : IHandler<GetDashboardReportQuery, Result<DashboardReportDto>>,
      IHandler<SetMyDashboardReportCommand, Result>,
      IHandler<SetOrganisationDashboardReportCommand, Result>
{
    private static readonly Error NotFound = Error.NotFound("reports.not_found", "The report does not exist or is not shared with you.");

    public async Task<Result<DashboardReportDto>> HandleAsync(GetDashboardReportQuery query, CancellationToken cancellationToken)
    {
        var me = access.Me;
        var choices = await db.Set<DashboardReport>().AsNoTracking().Where(d => d.UserId == me || d.UserId == null).ToListAsync(cancellationToken);
        var mine = choices.FirstOrDefault(d => d.UserId == me)?.ReportId;
        var organisation = choices.FirstOrDefault(d => d.UserId == null)?.ReportId;
        var ids = choices.Select(d => d.ReportId).ToList();
        var visible = await (await access.VisibleAsync(cancellationToken)).Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken);
        var shown = mine is { } m && visible.Contains(m) ? m : organisation is { } o && visible.Contains(o) ? o : (Guid?)null;
        return new DashboardReportDto(shown, mine, organisation);
    }

    public Task<Result> HandleAsync(SetMyDashboardReportCommand command, CancellationToken cancellationToken) =>
        SetAsync(access.Me, command.ReportId, cancellationToken);

    public Task<Result> HandleAsync(SetOrganisationDashboardReportCommand command, CancellationToken cancellationToken) =>
        SetAsync(null, command.ReportId, cancellationToken);

    private async Task<Result> SetAsync(Guid? userId, Guid? reportId, CancellationToken cancellationToken)
    {
        if (reportId is { } id && !await (await access.VisibleAsync(cancellationToken)).AnyAsync(r => r.Id == id, cancellationToken))
        {
            return NotFound;
        }

        var row = await db.Set<DashboardReport>().FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken);
        if (reportId is null)
        {
            if (row is not null)
            {
                db.Remove(row);
            }
        }
        else if (row is null)
        {
            db.Add(new DashboardReport
            {
                TenantId = currentUser.TenantId ?? throw new InvalidOperationException("Reports belong to an organisation."),
                UserId = userId,
                ReportId = reportId.Value,
            });
        }
        else
        {
            row.ReportId = reportId.Value;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
