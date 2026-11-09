using Coworkee.Application.Privacy;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing.Requests;

/// <summary>
/// The user's latest requests (what, when, from where; headers and bodies stay out of the export). Erasing keeps the rows without him: his id becomes the
/// erased user, address, browser, headers and bodies go.
/// </summary>
internal sealed class RequestLogPersonalData(CoworkeeDbContext db) : IPersonalDataContributor
{
    // ponytail: the latest requests only; a log of every read of a month would dwarf the rest of the export
    private const int Exported = 1000;

    public string Section => "requests";

    public async Task<object?> ExportAsync(PersonalDataSubject subject, CancellationToken cancellationToken) =>
        await Mine(subject).AsNoTracking().OrderByDescending(e => e.At).Take(Exported)
            .Select(e => new { e.At, e.Method, e.Path, e.StatusCode, e.Ip, e.UserAgent, AsOtherUser = e.ImpersonatorId == subject.UserId })
            .ToListAsync(cancellationToken);

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var id = subject.UserId;
        var erased = (Guid?)PersonalDataSubject.ErasedUserId;
        await Mine(subject).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.UserId, e => e.UserId == id ? erased : e.UserId)
            .SetProperty(e => e.ImpersonatorId, e => e.ImpersonatorId == id ? erased : e.ImpersonatorId)
            .SetProperty(e => e.Ip, (string?)null)
            .SetProperty(e => e.UserAgent, (string?)null)
            .SetProperty(e => e.RequestHeaders, (string?)null)
            .SetProperty(e => e.ResponseHeaders, (string?)null)
            .SetProperty(e => e.RequestBody, (string?)null)
            .SetProperty(e => e.ResponseBody, (string?)null), cancellationToken);
    }

    private IQueryable<RequestLogEntry> Mine(PersonalDataSubject subject) =>
        db.Set<RequestLogEntry>().Where(e => e.UserId == subject.UserId || e.ImpersonatorId == subject.UserId);
}
