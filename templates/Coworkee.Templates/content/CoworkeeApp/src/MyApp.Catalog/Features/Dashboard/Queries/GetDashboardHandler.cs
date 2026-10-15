using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Dashboard;
using MyApp.Catalog.Domain;
using MyApp.Contracts.Catalog;

namespace MyApp.Catalog.Features.Dashboard.Queries;

internal sealed class GetDashboardHandler(CoworkeeDbContext db, TimeProvider clock, IEnumerable<IDashboardCounts> documents)
    : IHandler<GetDashboardQuery, Result<DashboardDto>>
{
    private const int Months = 12;

    public async Task<Result<DashboardDto>> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1 - Months);
        // grouped in memory: SQLite cannot translate the date parts of a DateTimeOffset
        var created = (await db.Set<Product>().Where(p => p.CreatedAt >= start).Select(p => p.CreatedAt).ToListAsync(cancellationToken))
            .CountBy(c => (c.UtcDateTime.Year, c.UtcDateTime.Month))
            .ToDictionary();
        var perMonth = Enumerable.Range(0, Months)
            .Select(offset => start.AddMonths(offset))
            .Select(month => new MonthCountDto(month.Year, month.Month, created.GetValueOrDefault((month.Year, month.Month))))
            .ToList();
        var (documentCount, typeCount) = documents.FirstOrDefault() is { } counts ? await counts.DocumentsAsync(cancellationToken) : (0, 0);
        return new DashboardDto(
            await db.Set<Brand>().CountAsync(cancellationToken),
            await db.Set<Product>().CountAsync(cancellationToken),
            documentCount,
            typeCount,
            await db.Set<User>().CountAsync(cancellationToken),
            await db.Set<Role>().CountAsync(cancellationToken),
            perMonth);
    }
}
