using Coworkee.Application.Privacy;
using Coworkee.Contracts.Reporting;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Reporting.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Reporting;

/// <summary>
/// The user's reports, favourites, subscriptions and dashboard choice. Erasing deletes all of it but the reports he shared: those stay for the people
/// they are shared with, owned by <see cref="PersonalDataSubject.ErasedUserId"/>, so nobody changes them any more.
/// </summary>
internal sealed class ReportPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    public string Section => "reports";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var userId = subject.UserId;
        return new
        {
            Reports = await Owned(userId).AsNoTracking().OrderBy(r => r.CreatedAt)
                .Select(r => new { r.Id, r.Name, r.Description, r.Visibility, r.CreatedAt, r.ModifiedAt }).ToListAsync(cancellationToken),
            Favorites = await (from favorite in db.Set<ReportFavorite>().AsNoTracking()
                               join report in db.Set<Report>().IgnoreQueryFilters() on favorite.ReportId equals report.Id
                               where favorite.UserId == userId
                               select new { report.Id, report.Name }).ToListAsync(cancellationToken),
            Subscriptions = await (from subscription in db.Set<ReportSubscription>().IgnoreQueryFilters().AsNoTracking()
                                   join report in db.Set<Report>().IgnoreQueryFilters() on subscription.ReportId equals report.Id
                                   where subscription.UserId == userId
                                   select new
                                   {
                                       Report = report.Name, subscription.Frequency, subscription.Time, subscription.TimeZone, subscription.Weekday, subscription.DayOfMonth,
                                       subscription.Every, subscription.Week, subscription.Filter, subscription.Attachment, subscription.CreatedAt, subscription.LastSentAt,
                                   }).ToListAsync(cancellationToken),
            Dashboard = await db.Set<DashboardReport>().IgnoreQueryFilters().AsNoTracking().Where(d => d.UserId == userId)
                .Select(d => (Guid?)d.ReportId).FirstOrDefaultAsync(cancellationToken),
        };
    }

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var userId = subject.UserId;
        await db.Set<ReportSubscription>().IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<ReportFavorite>().Where(f => f.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<DashboardReport>().IgnoreQueryFilters().Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // favourites, subscriptions and dashboards of others on his private reports go with them by cascade
        await Owned(userId).Where(r => r.Visibility == ReportVisibility.Private).ExecuteDeleteAsync(cancellationToken);
        await Owned(userId).ExecuteUpdateAsync(s => s.SetProperty(r => r.OwnerId, PersonalDataSubject.ErasedUserId), cancellationToken);
    }

    private IQueryable<Report> Owned(Guid userId) => db.Set<Report>().IgnoreQueryFilters().Where(r => r.OwnerId == userId);
}
